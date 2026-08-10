using System.Globalization;
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
    private readonly IIdentityRepository _repository;
    private readonly IPlatformTransactionExecutor _transactionExecutor;
    private readonly IAuthenticationSessionRegistry _sessionRegistry;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITemporaryCredentialIssuer _temporaryCredentialIssuer;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly TimeProvider _timeProvider;

    public IdentityAccountApplicationService(
        IIdentityRepository repository,
        IPlatformTransactionExecutor transactionExecutor,
        IAuthenticationSessionRegistry sessionRegistry,
        IPasswordHasher<User> passwordHasher,
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
        var ecode = NormalizeAccountCode(command.ECODE);
        var normalizedCode = ecode.ToUpperInvariant();
        var ename = NormalizeDisplayName(command.ENAME);
        ValidateActor(command.Actor);

        if (await _repository.FindUserByNormalizedCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.AccountCodeConflict);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            EID = _objectIdGenerator.NewId(nameof(User)),
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
            UserEID = user.EID,
            PasswordHash = _passwordHasher.HashPassword(user, temporaryCredential),
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
                    _repository.AddUser(user);
                    _repository.AddCredential(credential);
                    _repository.AddSecurityEvent(CreateEvent(
                        user.EID,
                        IdentitySecurityEventTypes.AccountCreated,
                        "创建账号",
                        command.Actor,
                        now));
                    return ValueTask.FromResult(new IdentityAccountCreationResult(ToSnapshot(user), temporaryCredential));
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
        var ename = NormalizeDisplayName(command.ENAME);
        var user = await GetRequiredUserAsync(command.UserEid, cancellationToken);
        EnsureNotDeleted(user);
        EnsureExpectedRevision(user, command.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.ENAME = ename;
        StampSecurityChange(user, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceUserOrThrowConflictAsync(user, expectedRevision, token);
                _repository.AddSecurityEvent(CreateEvent(
                    user.EID,
                    IdentitySecurityEventTypes.DisplayNameChanged,
                    "修改显示名称",
                    command.Actor,
                    now));
                return ToSnapshot(user);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityAccountSnapshot> SetStatusAsync(
        SetIdentityAccountStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = NormalizeReason(command.Reason);
        if (command.TargetStatus is not IdentityAccountStatus.Enabled and not IdentityAccountStatus.Disabled)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidTargetStatus);
        }

        var user = await GetRequiredUserAsync(command.UserEid, cancellationToken);
        EnsureNotDeleted(user);
        if (user.Status == command.TargetStatus)
        {
            return ToSnapshot(user);
        }

        if (user.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(user));
        }

        if (command.TargetStatus == IdentityAccountStatus.Enabled && user.PersonnelDisableHold)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.PersonnelDisableHold, ToSnapshot(user));
        }

        EnsureExpectedRevision(user, command.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.Status = command.TargetStatus;
        StampSecurityChange(user, command.Actor.ActorId, now);
        var eventType = command.TargetStatus == IdentityAccountStatus.Enabled
            ? IdentitySecurityEventTypes.AccountEnabled
            : IdentitySecurityEventTypes.AccountDisabled;

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceUserOrThrowConflictAsync(user, expectedRevision, token);
                if (command.TargetStatus == IdentityAccountStatus.Disabled)
                {
                    await RevokeAllSessionsAsync(user.EID, reason, command.Actor.ActorId, token);
                }

                _repository.AddSecurityEvent(CreateEvent(user.EID, eventType, reason, command.Actor, now));
                return ToSnapshot(user);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(
        ResetIdentityCredentialCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = NormalizeReason(command.Reason);
        var user = await GetRequiredUserAsync(command.UserEid, cancellationToken);
        EnsureNotDeleted(user);
        if (user.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(user));
        }

        EnsureExpectedRevision(user, command.ExpectedRevision);
        var credential = await GetRequiredCredentialAsync(user.EID, cancellationToken);
        var temporaryCredential = _temporaryCredentialIssuer.Issue();
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        credential.PasswordHash = _passwordHasher.HashPassword(user, temporaryCredential);
        credential.IsTemporary = true;
        credential.UpdatedBy = command.Actor.ActorId;
        credential.UpdatedAt = now;
        StampSecurityChange(user, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceUserOrThrowConflictAsync(user, expectedRevision, token);
                await _repository.ReplaceCredentialAsync(credential, token);
                await RevokeAllSessionsAsync(user.EID, reason, command.Actor.ActorId, token);
                _repository.AddSecurityEvent(CreateEvent(
                    user.EID,
                    IdentitySecurityEventTypes.CredentialReset,
                    reason,
                    command.Actor,
                    now));
                return new IdentityCredentialResetResult(ToSnapshot(user), temporaryCredential);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityAccountSnapshot> ChangePasswordAsync(
        ChangeIdentityPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        ValidatePermanentPassword(command.NewPassword);
        var user = await GetRequiredUserAsync(command.UserEid, cancellationToken);
        EnsureNotDeleted(user);
        EnsureExpectedRevision(user, command.ExpectedRevision);
        var credential = await GetRequiredCredentialAsync(user.EID, cancellationToken);
        if (_passwordHasher.VerifyHashedPassword(user, credential.PasswordHash, command.CurrentPassword)
            == PasswordVerificationResult.Failed)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidCurrentCredential);
        }

        if (_passwordHasher.VerifyHashedPassword(user, credential.PasswordHash, command.NewPassword)
            != PasswordVerificationResult.Failed)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.CredentialUnchanged);
        }

        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        credential.PasswordHash = _passwordHasher.HashPassword(user, command.NewPassword);
        credential.IsTemporary = false;
        credential.UpdatedBy = command.Actor.ActorId;
        credential.UpdatedAt = now;
        StampSecurityChange(user, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceUserOrThrowConflictAsync(user, expectedRevision, token);
                await _repository.ReplaceCredentialAsync(credential, token);
                await RevokeAllSessionsAsync(user.EID, "password-changed", command.Actor.ActorId, token);
                _repository.AddSecurityEvent(CreateEvent(
                    user.EID,
                    IdentitySecurityEventTypes.PasswordChanged,
                    "用户修改密码",
                    command.Actor,
                    now));
                return ToSnapshot(user);
            },
            cancellationToken);
    }

    public async ValueTask<IdentityAccountSnapshot> DeleteAsync(
        DeleteIdentityAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.Actor);
        var reason = NormalizeReason(command.Reason);
        var user = await GetRequiredUserAsync(command.UserEid, cancellationToken);
        if (user.Status == IdentityAccountStatus.Deleted)
        {
            return ToSnapshot(user);
        }

        if (user.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.LocalAdministratorProtected, ToSnapshot(user));
        }

        if (user.IsPersonnelBound)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.PersonnelBound, ToSnapshot(user));
        }

        EnsureExpectedRevision(user, command.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.Status = IdentityAccountStatus.Deleted;
        StampSecurityChange(user, command.Actor.ActorId, now);

        return await _transactionExecutor.ExecuteAsync(
            async token =>
            {
                await ReplaceUserOrThrowConflictAsync(user, expectedRevision, token);
                await _repository.RemoveCredentialAsync(user.EID, token);
                await RevokeAllSessionsAsync(user.EID, reason, command.Actor.ActorId, token);
                _repository.AddSecurityEvent(CreateEvent(
                    user.EID,
                    IdentitySecurityEventTypes.AccountDeleted,
                    reason,
                    command.Actor,
                    now));
                return ToSnapshot(user);
            },
            cancellationToken);
    }

    internal static IdentityAccountSnapshot ToSnapshot(User user) => new()
    {
        UserEid = user.EID,
        ECODE = user.ECODE,
        ENAME = user.ENAME,
        Status = user.Status,
        Revision = user.Revision,
        IsLocalAdministrator = user.IsLocalAdministrator,
        IsPersonnelBound = user.IsPersonnelBound,
        PersonnelDisableHold = user.PersonnelDisableHold
    };

    private IdentitySecurityEvent CreateEvent(
        string targetUserEid,
        string eventType,
        string reason,
        IdentityActorContext actor,
        DateTime occurredAt) => new()
    {
        EID = _objectIdGenerator.NewId(nameof(IdentitySecurityEvent)),
        TargetUserEID = targetUserEid,
        EventType = eventType,
        Reason = reason,
        OccurredAt = occurredAt,
        TraceId = actor.TraceId,
        ActorKind = actor.ActorKind,
        ActorUserEID = actor.ActorKind == IdentitySecurityActorKind.User ? actor.ActorId : null,
        CreatedBy = actor.ActorId,
        CreatedAt = occurredAt,
        UpdatedBy = actor.ActorId,
        UpdatedAt = occurredAt
    };

    private static string NormalizeAccountCode(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        var runes = normalized.EnumerateRunes().ToArray();
        if (runes.Length is < 3 or > 64 || runes.Any(static rune => !IsAllowedAccountCodeRune(rune)))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidAccountCode);
        }

        return normalized;
    }

    private static bool IsAllowedAccountCodeRune(Rune rune) =>
        Rune.IsLetterOrDigit(rune)
        || rune.Value is '.' or '_' or '-' or '@';

    private static string NormalizeDisplayName(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 128)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidDisplayName);
        }

        return normalized;
    }

    private static string NormalizeReason(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 500)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidReason);
        }

        return normalized;
    }

    private static void ValidatePermanentPassword(string password)
    {
        var value = password ?? string.Empty;
        var length = value.EnumerateRunes().Count();
        if (length is < 8 or > 64 || value.EnumerateRunes().Any(Rune.IsWhiteSpace))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidPassword);
        }
    }

    private async ValueTask<User> GetRequiredUserAsync(string userEid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userEid))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);
        }

        return await _repository.FindUserAsync(userEid, cancellationToken)
            ?? throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);
    }

    private async ValueTask<LocalCredential> GetRequiredCredentialAsync(string userEid, CancellationToken cancellationToken) =>
        await _repository.FindCredentialAsync(userEid, cancellationToken)
        ?? throw new IdentityApplicationException(IdentityFailureCodes.InvalidCurrentCredential);

    private static void EnsureNotDeleted(User user)
    {
        if (user.Status == IdentityAccountStatus.Deleted)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.DeletedAccount, ToSnapshot(user));
        }
    }

    private static void EnsureExpectedRevision(User user, long expectedRevision)
    {
        if (user.Revision != expectedRevision)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.RevisionConflict, ToSnapshot(user));
        }
    }

    private static void StampSecurityChange(User user, string actorId, DateTime occurredAt)
    {
        user.Revision++;
        user.UpdatedBy = actorId;
        user.UpdatedAt = occurredAt;
    }

    private async ValueTask ReplaceUserOrThrowConflictAsync(
        User user,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (await _repository.TryReplaceUserAsync(user, expectedRevision, cancellationToken))
        {
            return;
        }

        var latest = await _repository.FindUserAsync(user.EID, cancellationToken);
        throw new IdentityApplicationException(
            IdentityFailureCodes.RevisionConflict,
            latest is null ? null : ToSnapshot(latest));
    }

    private async ValueTask RevokeAllSessionsAsync(
        string userEid,
        string reason,
        string actorId,
        CancellationToken cancellationToken) =>
        _ = await _sessionRegistry.RevokeAllAsync(
            new UserAuthenticationSessionsRevocation
            {
                UserEid = userEid,
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
            if (current.Message.Contains("UX_User_NormalizedECODE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
