using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class IdentityAccountApplicationService : IIdentityAccountApplicationService
{
    // 账号与会话撤销仅在平台事务回调内登记；Identity 与 Authentication 的实现都不得在阶段中自行 SaveChanges。
    private readonly IIdentityRepository _repository;
    private readonly IPlatformTransactionExecutor _transactionExecutor;
    private readonly IAuthenticationSessionRegistry _sessionRegistry;
    private readonly IPasswordHasher<IdentityAccount> _passwordHasher;
    private readonly ITemporaryCredentialIssuer _temporaryCredentialIssuer;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly TimeProvider _timeProvider;

    public IdentityAccountApplicationService(
        IIdentityRepository repository,
        IPlatformTransactionExecutor transactionExecutor,
        IAuthenticationSessionRegistry sessionRegistry,
        IPasswordHasher<IdentityAccount> passwordHasher,
        ITemporaryCredentialIssuer temporaryCredentialIssuer,
        IPlatformObjectIdGenerator objectIdGenerator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _transactionExecutor = transactionExecutor;
        _sessionRegistry = sessionRegistry;
        _passwordHasher = passwordHasher;
        _temporaryCredentialIssuer = temporaryCredentialIssuer;
        _objectIdGenerator = objectIdGenerator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IdentityAccountCreationResult> CreateAsync(
        CreateIdentityAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var ecode = IdentityInputNormalizer.NormalizeAccountCode(command.ECODE);
        var normalizedCode = ecode.ToUpperInvariant();
        var ename = IdentityInputNormalizer.NormalizeDisplayName(command.ENAME);
        ValidateActor(command.Actor);

        if (await _repository.FindAccountByNormalizedCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.AccountCodeConflict);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var account = new IdentityAccount
        {
            EID = _objectIdGenerator.NewId(nameof(IdentityAccount)),
            ECODE = ecode,
            NormalizedECODE = normalizedCode,
            ENAME = ename,
            Status = IdentityAccountStatus.Enabled,
            Revision = 1,
            CreatedBy = command.Actor.ActorId,
            CreatedAt = now,
            UpdatedBy = command.Actor.ActorId,
            UpdatedAt = now
        };
        var temporaryCredential = _temporaryCredentialIssuer.Issue();
        var credential = new LocalCredential
        {
            EID = _objectIdGenerator.NewId(nameof(LocalCredential)),
            AccountEID = account.EID,
            PasswordHash = _passwordHasher.HashPassword(account, temporaryCredential),
            IsTemporary = true,
            CreatedBy = command.Actor.ActorId,
            CreatedAt = now,
            UpdatedBy = command.Actor.ActorId,
            UpdatedAt = now
        };

        try
        {
            return await _transactionExecutor.ExecuteAsync(
                _ =>
                {
                    _repository.AddAccount(account);
                    _repository.AddCredential(credential);
                    _repository.AddSecurityEvent(CreateEvent(
                        account.EID,
                        IdentitySecurityEventTypes.AccountCreated,
                        "创建账号",
                        command.Actor,
                        now));
                    return ValueTask.FromResult(new IdentityAccountCreationResult(ToSnapshot(account), temporaryCredential));
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAccountCodeConflict(exception))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.AccountCodeConflict, innerException: exception);
        }
    }

    public async ValueTask<IdentityAccountSnapshot> UpdateNameAsync(
        UpdateIdentityAccountNameCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var ename = IdentityInputNormalizer.NormalizeDisplayName(command.ENAME);
        var account = await GetRequiredAccountAsync(command.AccountEid, cancellationToken);
        EnsureNotDeleted(account);
        EnsureExpectedRevision(account, command.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.ENAME = ename;
        StampSecurityChange(account, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, token);
                _repository.AddSecurityEvent(CreateEvent(
                    account.EID,
                    IdentitySecurityEventTypes.DisplayNameChanged,
                    "修改显示名称",
                    command.Actor,
                    now));
                return ToSnapshot(account);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityAccountSnapshot> SetStatusAsync(
        SetIdentityAccountStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = IdentityInputNormalizer.NormalizeReason(command.Reason);
        if (command.TargetStatus is not IdentityAccountStatus.Enabled and not IdentityAccountStatus.Disabled)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidTargetStatus);
        }

        var account = await GetRequiredAccountAsync(command.AccountEid, cancellationToken);
        EnsureNotDeleted(account);
        if (account.Status == command.TargetStatus)
        {
            return ToSnapshot(account);
        }

        if (account.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(account));
        }

        if (command.TargetStatus == IdentityAccountStatus.Enabled && account.PersonnelDisableHold)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.PersonnelDisableHold, ToSnapshot(account));
        }

        EnsureExpectedRevision(account, command.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.Status = command.TargetStatus;
        StampSecurityChange(account, command.Actor.ActorId, now);
        var eventType = command.TargetStatus == IdentityAccountStatus.Enabled
            ? IdentitySecurityEventTypes.AccountEnabled
            : IdentitySecurityEventTypes.AccountDisabled;

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, token);
                if (command.TargetStatus == IdentityAccountStatus.Disabled)
                {
                    await RevokeAllSessionsAsync(account.EID, reason, command.Actor.ActorId, token);
                }

                _repository.AddSecurityEvent(CreateEvent(account.EID, eventType, reason, command.Actor, now));
                return ToSnapshot(account);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(
        ResetIdentityCredentialCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = IdentityInputNormalizer.NormalizeReason(command.Reason);
        var account = await GetRequiredAccountAsync(command.AccountEid, cancellationToken);
        EnsureNotDeleted(account);
        if (account.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(account));
        }

        EnsureExpectedRevision(account, command.ExpectedRevision);
        var credential = await GetRequiredCredentialAsync(account.EID, cancellationToken);
        var temporaryCredential = _temporaryCredentialIssuer.Issue();
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        credential.PasswordHash = _passwordHasher.HashPassword(account, temporaryCredential);
        credential.IsTemporary = true;
        credential.UpdatedBy = command.Actor.ActorId;
        credential.UpdatedAt = now;
        StampSecurityChange(account, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, token);
                await _repository.ReplaceCredentialAsync(credential, token);
                await RevokeAllSessionsAsync(account.EID, reason, command.Actor.ActorId, token);
                _repository.AddSecurityEvent(CreateEvent(
                    account.EID,
                    IdentitySecurityEventTypes.CredentialReset,
                    reason,
                    command.Actor,
                    now));
                return new IdentityCredentialResetResult(ToSnapshot(account), temporaryCredential);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityPasswordChangeResult> ChangePasswordAsync(
        ChangeIdentityPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        ValidatePermanentPassword(command.NewPassword);
        var account = await GetRequiredAccountAsync(command.AccountEid, cancellationToken);
        EnsureNotDeleted(account);
        EnsureExpectedRevision(account, command.ExpectedRevision);
        var credential = await GetRequiredCredentialAsync(account.EID, cancellationToken);
        if (_passwordHasher.VerifyHashedPassword(account, credential.PasswordHash, command.CurrentPassword)
            == PasswordVerificationResult.Failed)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidCurrentCredential);
        }

        if (_passwordHasher.VerifyHashedPassword(account, credential.PasswordHash, command.NewPassword)
            != PasswordVerificationResult.Failed)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.CredentialUnchanged);
        }

        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        credential.PasswordHash = _passwordHasher.HashPassword(account, command.NewPassword);
        credential.IsTemporary = false;
        credential.UpdatedBy = command.Actor.ActorId;
        credential.UpdatedAt = now;
        StampSecurityChange(account, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, token);
                await _repository.ReplaceCredentialAsync(credential, token);
                await RevokeAllSessionsAsync(account.EID, "password-changed", command.Actor.ActorId, token);
                var session = await _sessionRegistry.RegisterAsync(
                    new AuthenticationSessionRegistration
                    {
                        AccountEid = account.EID,
                        Type = AuthenticationSessionType.Regular,
                        ActorId = command.Actor.ActorId
                    },
                    token);
                _repository.AddSecurityEvent(CreateEvent(
                    account.EID,
                    IdentitySecurityEventTypes.PasswordChanged,
                    "用户修改密码",
                    command.Actor,
                    now));
                return new IdentityPasswordChangeResult(ToSnapshot(account), session);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityAccountSnapshot> DeleteAsync(
        DeleteIdentityAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = IdentityInputNormalizer.NormalizeReason(command.Reason);
        var account = await GetRequiredAccountAsync(command.AccountEid, cancellationToken);
        if (account.Status == IdentityAccountStatus.Deleted)
        {
            return ToSnapshot(account);
        }

        if (account.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(account));
        }

        if (account.IsPersonnelBound)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.PersonnelBound, ToSnapshot(account));
        }

        EnsureExpectedRevision(account, command.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.Status = IdentityAccountStatus.Deleted;
        StampSecurityChange(account, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, token);
                await _repository.RemoveCredentialAsync(account.EID, token);
                await RevokeAllSessionsAsync(account.EID, reason, command.Actor.ActorId, token);
                _repository.AddSecurityEvent(CreateEvent(
                    account.EID,
                    IdentitySecurityEventTypes.AccountDeleted,
                    reason,
                    command.Actor,
                    now));
                return ToSnapshot(account);
            },
            cancellationToken);
    }

    internal static IdentityAccountSnapshot ToSnapshot(IdentityAccount account) => new()
    {
        AccountEid = account.EID,
        ECODE = account.ECODE,
        ENAME = account.ENAME,
        Status = account.Status,
        Revision = account.Revision,
        IsLocalAdministrator = account.IsLocalAdministrator,
        IsPersonnelBound = account.IsPersonnelBound,
        PersonnelDisableHold = account.PersonnelDisableHold
    };

    private IdentitySecurityEvent CreateEvent(
        string targetAccountEid,
        string eventType,
        string reason,
        IdentityActorContext actor,
        DateTime occurredAt) => new()
        {
            EID = _objectIdGenerator.NewId(nameof(IdentitySecurityEvent)),
            TargetAccountEID = targetAccountEid,
            EventType = eventType,
            Reason = reason,
            OccurredAt = occurredAt,
            TraceId = actor.TraceId,
            ActorKind = actor.ActorKind,
            ActorAccountEID = actor.ActorKind == IdentitySecurityActorKind.IdentityAccount ? actor.ActorId : null,
            CreatedBy = actor.ActorId,
            CreatedAt = occurredAt,
            UpdatedBy = actor.ActorId,
            UpdatedAt = occurredAt
        };

    private static void ValidatePermanentPassword(string password)
    {
        var value = password ?? string.Empty;
        var length = value.EnumerateRunes().Count();
        if (length is < 8 or > 64 || value.EnumerateRunes().Any(Rune.IsWhiteSpace))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidPassword);
        }
    }

    private async ValueTask<IdentityAccount> GetRequiredAccountAsync(string accountEid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountEid))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);
        }

        return await _repository.FindAccountAsync(accountEid, cancellationToken)
            ?? throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);
    }

    private async ValueTask<LocalCredential> GetRequiredCredentialAsync(string accountEid, CancellationToken cancellationToken) =>
        await _repository.FindCredentialAsync(accountEid, cancellationToken)
        ?? throw new IdentityApplicationException(IdentityFailureCodes.InvalidCurrentCredential);

    private static void EnsureNotDeleted(IdentityAccount account)
    {
        if (account.Status == IdentityAccountStatus.Deleted)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.DeletedAccount, ToSnapshot(account));
        }
    }

    private static void EnsureExpectedRevision(IdentityAccount account, long expectedRevision)
    {
        if (account.Revision != expectedRevision)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.RevisionConflict, ToSnapshot(account));
        }
    }

    private static void StampSecurityChange(IdentityAccount account, string actorId, DateTime occurredAt)
    {
        account.Revision++;
        account.UpdatedBy = actorId;
        account.UpdatedAt = occurredAt;
    }

    private async ValueTask ReplaceAccountOrThrowConflictAsync(
        IdentityAccount account,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (await _repository.TryReplaceAccountAsync(account, expectedRevision, cancellationToken))
        {
            return;
        }

        var latest = await _repository.FindAccountAsync(account.EID, cancellationToken);
        throw new IdentityApplicationException(
            IdentityFailureCodes.RevisionConflict,
            latest is null ? null : ToSnapshot(latest));
    }

    private async ValueTask RevokeAllSessionsAsync(
        string accountEid,
        string reason,
        string actorId,
        CancellationToken cancellationToken) =>
        _ = await _sessionRegistry.RevokeAllAsync(
            new AccountAuthenticationSessionsRevocation
            {
                AccountEid = accountEid,
                Reason = reason,
                ActorId = actorId
            },
            cancellationToken);

    private static void ValidateActor(IdentityActorContext actor)
    {
        if (actor is null
            || string.IsNullOrWhiteSpace(actor.ActorId)
            || string.IsNullOrWhiteSpace(actor.TraceId)
            || !Enum.IsDefined(actor.ActorKind))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidActor);
        }
    }

    private static bool IsAccountCodeConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("UX_IdentityAccount_NormalizedECODE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
