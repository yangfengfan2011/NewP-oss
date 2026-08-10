using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace ICP.Platform.Modules.Identity.Application;

public static class IdentityMaintenanceActors
{
    public const string LocalMaintenance = "platform:local-maintenance";
}

public static class IdentityMaintenanceFailureCodes
{
    public const string BootstrapAlreadyCompleted = "IDENTITY_BOOTSTRAP_ALREADY_COMPLETED";
    public const string LocalAdministratorInvariant = "IDENTITY_LOCAL_ADMIN_INVARIANT";
}

public static class IdentityMaintenanceReasons
{
    public const string Bootstrap = "离线初始化本地管理员";
    public const string CredentialReset = "离线重置本地管理员凭据";
}

public sealed record BootstrapLocalAdministratorCommand(
    string ECODE,
    string ENAME,
    string Reason,
    IdentityActorContext Actor);

public sealed record ResetLocalAdministratorCredentialCommand(
    string Reason,
    IdentityActorContext Actor);

public sealed class IdentityMaintenanceException : InvalidOperationException
{
    public IdentityMaintenanceException(string failureCode) : base(failureCode)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}

public interface IIdentityLocalAdministratorMaintenanceService
{
    ValueTask<IdentityAccountCreationResult> BootstrapAsync(
        BootstrapLocalAdministratorCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityCredentialResetResult> ResetAsync(
        ResetLocalAdministratorCredentialCommand command,
        CancellationToken cancellationToken = default);
}

public enum LocalAdministratorReadinessState
{
    Ready,
    BootstrapRequired,
    InvariantInvalid
}

public sealed record LocalAdministratorReadiness(
    LocalAdministratorReadinessState State,
    string Diagnostic,
    string? UserEid = null);

public interface IIdentityLocalAdministratorReadinessProbe
{
    ValueTask<LocalAdministratorReadiness> InspectAsync(CancellationToken cancellationToken = default);
}

public sealed class IdentityLocalAdministratorReadinessProbe(
    IIdentityRepository repository,
    ILocalAdministratorRoleAssignment roleAssignment) : IIdentityLocalAdministratorReadinessProbe
{
    public async ValueTask<LocalAdministratorReadiness> InspectAsync(CancellationToken cancellationToken = default)
    {
        var users = await repository.SearchUsersAsync(cancellationToken);
        if (users.Count == 0)
        {
            return new(LocalAdministratorReadinessState.BootstrapRequired, "identity-bootstrap-required");
        }

        var administrators = users.Where(static user => user.IsLocalAdministrator).ToArray();
        if (administrators.Length != 1)
        {
            return new(LocalAdministratorReadinessState.InvariantInvalid, "local-administrator-count-invalid");
        }

        var administrator = administrators[0];
        if (administrator.Status != IdentityAccountStatus.Enabled
            || await repository.FindCredentialAsync(administrator.EID, cancellationToken) is null)
        {
            return new(LocalAdministratorReadinessState.InvariantInvalid, "local-administrator-credential-invalid", administrator.EID);
        }

        if (await roleAssignment.InspectAsync(administrator.EID, cancellationToken)
            != LocalAdministratorAssignmentState.Assigned)
        {
            return new(LocalAdministratorReadinessState.InvariantInvalid, "local-admin-role-assignment-invalid", administrator.EID);
        }

        return new(LocalAdministratorReadinessState.Ready, "local-administrator-ready", administrator.EID);
    }
}

public sealed class IdentityLocalAdministratorMaintenanceService(
    IIdentityRepository repository,
    IPlatformTransactionExecutor transactionExecutor,
    IAuthenticationSessionRegistry sessionRegistry,
    ILocalAdministratorRoleAssignment roleAssignment,
    IPasswordHasher<User> passwordHasher,
    ITemporaryCredentialIssuer temporaryCredentialIssuer,
    IPlatformObjectIdGenerator objectIdGenerator,
    TimeProvider timeProvider) : IIdentityLocalAdministratorMaintenanceService
{
    public ValueTask<IdentityAccountCreationResult> BootstrapAsync(
        BootstrapLocalAdministratorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateMaintenanceActor(command.Actor);
        var ecode = IdentityInputNormalizer.NormalizeAccountCode(command.ECODE);
        var ename = IdentityInputNormalizer.NormalizeDisplayName(command.ENAME);
        var reason = IdentityInputNormalizer.NormalizeReason(command.Reason);

        return transactionExecutor.ExecuteAsync(async token =>
        {
            await repository.AcquireLocalAdministratorBootstrapLockAsync(token);
            if ((await repository.SearchUsersAsync(token)).Count != 0)
            {
                throw new IdentityMaintenanceException(IdentityMaintenanceFailureCodes.BootstrapAlreadyCompleted);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var user = new User
            {
                EID = objectIdGenerator.NewId(nameof(User)),
                ECODE = ecode,
                NormalizedECODE = ecode.ToUpperInvariant(),
                ENAME = ename,
                Status = IdentityAccountStatus.Enabled,
                IsLocalAdministrator = true,
                Revision = 1,
                CreatedBy = command.Actor.ActorId,
                CreatedAt = now,
                UpdatedBy = command.Actor.ActorId,
                UpdatedAt = now
            };
            var temporaryCredential = temporaryCredentialIssuer.Issue();
            repository.AddUser(user);
            repository.AddCredential(new LocalCredential
            {
                EID = objectIdGenerator.NewId(nameof(LocalCredential)),
                UserEID = user.EID,
                PasswordHash = passwordHasher.HashPassword(user, temporaryCredential),
                IsTemporary = true,
                CreatedBy = command.Actor.ActorId,
                CreatedAt = now,
                UpdatedBy = command.Actor.ActorId,
                UpdatedAt = now
            });
            repository.AddSecurityEvent(CreateEvent(
                user.EID,
                IdentitySecurityEventTypes.LocalAdministratorBootstrapped,
                reason,
                command.Actor,
                now));
            await roleAssignment.AssignAsync(user.EID, command.Actor.ActorId, token);
            return new IdentityAccountCreationResult(IdentityAccountApplicationService.ToSnapshot(user), temporaryCredential);
        }, cancellationToken);
    }

    public async ValueTask<IdentityCredentialResetResult> ResetAsync(
        ResetLocalAdministratorCredentialCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateMaintenanceActor(command.Actor);
        var reason = IdentityInputNormalizer.NormalizeReason(command.Reason);
        var users = await repository.SearchUsersAsync(cancellationToken);
        var administrators = users.Where(static user => user.IsLocalAdministrator).ToArray();
        if (administrators.Length != 1)
        {
            throw new IdentityMaintenanceException(IdentityMaintenanceFailureCodes.LocalAdministratorInvariant);
        }

        var administrator = administrators[0];
        var credential = await repository.FindCredentialAsync(administrator.EID, cancellationToken)
            ?? throw new IdentityMaintenanceException(IdentityMaintenanceFailureCodes.LocalAdministratorInvariant);
        if (administrator.Status != IdentityAccountStatus.Enabled
            || await roleAssignment.InspectAsync(administrator.EID, cancellationToken) != LocalAdministratorAssignmentState.Assigned)
        {
            throw new IdentityMaintenanceException(IdentityMaintenanceFailureCodes.LocalAdministratorInvariant);
        }

        var temporaryCredential = temporaryCredentialIssuer.Issue();
        var expectedRevision = administrator.Revision;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        credential.PasswordHash = passwordHasher.HashPassword(administrator, temporaryCredential);
        credential.IsTemporary = true;
        credential.UpdatedBy = command.Actor.ActorId;
        credential.UpdatedAt = now;
        administrator.Revision++;
        administrator.UpdatedBy = command.Actor.ActorId;
        administrator.UpdatedAt = now;

        return await transactionExecutor.ExecuteAsync(async token =>
        {
            if (!await repository.TryReplaceUserAsync(administrator, expectedRevision, token))
            {
                throw new IdentityMaintenanceException(IdentityMaintenanceFailureCodes.LocalAdministratorInvariant);
            }

            await repository.ReplaceCredentialAsync(credential, token);
            await sessionRegistry.RevokeAllAsync(new UserAuthenticationSessionsRevocation
            {
                UserEid = administrator.EID,
                Reason = reason,
                ActorId = command.Actor.ActorId
            }, token);
            repository.AddSecurityEvent(CreateEvent(
                administrator.EID,
                IdentitySecurityEventTypes.LocalAdministratorCredentialReset,
                reason,
                command.Actor,
                now));
            return new IdentityCredentialResetResult(IdentityAccountApplicationService.ToSnapshot(administrator), temporaryCredential);
        }, cancellationToken);
    }

    private IdentitySecurityEvent CreateEvent(
        string userEid,
        string eventType,
        string reason,
        IdentityActorContext actor,
        DateTime now) => new()
        {
            EID = objectIdGenerator.NewId(nameof(IdentitySecurityEvent)),
            TargetUserEID = userEid,
            EventType = eventType,
            Reason = reason,
            OccurredAt = now,
            TraceId = actor.TraceId,
            ActorKind = actor.ActorKind,
            ActorUserEID = null,
            CreatedBy = actor.ActorId,
            CreatedAt = now,
            UpdatedBy = actor.ActorId,
            UpdatedAt = now
        };

    private static void ValidateMaintenanceActor(IdentityActorContext actor)
    {
        if (actor.ActorKind != IdentitySecurityActorKind.LocalMaintenance
            || !string.Equals(actor.ActorId, IdentityMaintenanceActors.LocalMaintenance, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(actor.TraceId))
        {
            throw new IdentityMaintenanceException(IdentityFailureCodes.InvalidActor);
        }
    }
}
