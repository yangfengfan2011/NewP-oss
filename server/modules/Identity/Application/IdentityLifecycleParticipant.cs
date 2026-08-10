using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class IdentityLifecycleParticipant : IIdentityLifecycleParticipant
{
    private readonly IIdentityRepository _repository;
    private readonly IAuthenticationSessionRegistry _sessionRegistry;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly TimeProvider _timeProvider;

    public IdentityLifecycleParticipant(
        IIdentityRepository repository,
        IAuthenticationSessionRegistry sessionRegistry,
        IPlatformObjectIdGenerator objectIdGenerator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _sessionRegistry = sessionRegistry;
        _objectIdGenerator = objectIdGenerator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(
        PersonnelBindingRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        if (string.IsNullOrWhiteSpace(registration.PersonEid))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidPersonReference);
        }

        var reason = NormalizeReason(registration.Reason);
        var user = await GetRequiredUserAsync(registration.UserEid, cancellationToken);
        EnsureAvailableOrdinaryUser(user);
        if (user.IsPersonnelBound)
        {
            return Result(user, changed: false);
        }

        EnsureRevision(user, registration.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.IsPersonnelBound = true;
        Stamp(user, registration.ActorId, now);
        await ReplaceUserOrThrowConflictAsync(user, expectedRevision, cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            user.EID,
            IdentitySecurityEventTypes.PersonnelBound,
            reason,
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(user, changed: true);
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(
        PersonnelAccountDisableRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        var reason = NormalizeReason(registration.Reason);
        var user = await GetRequiredUserAsync(registration.UserEid, cancellationToken);
        EnsureAvailableOrdinaryUser(user);
        if (user.Status == IdentityAccountStatus.Disabled && user.PersonnelDisableHold)
        {
            return Result(user, changed: false);
        }

        EnsureRevision(user, registration.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.Status = IdentityAccountStatus.Disabled;
        user.PersonnelDisableHold = true;
        Stamp(user, registration.ActorId, now);
        await ReplaceUserOrThrowConflictAsync(user, expectedRevision, cancellationToken);
        _ = await _sessionRegistry.RevokeAllAsync(
            new UserAuthenticationSessionsRevocation
            {
                UserEid = user.EID,
                Reason = reason,
                ActorId = registration.ActorId
            },
            cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            user.EID,
            IdentitySecurityEventTypes.PersonnelDisabled,
            reason,
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(user, changed: true);
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(
        PersonnelDisableHoldReleaseRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        var user = await GetRequiredUserAsync(registration.UserEid, cancellationToken);
        EnsureAvailableOrdinaryUser(user);
        if (!user.PersonnelDisableHold)
        {
            return Result(user, changed: false);
        }

        EnsureRevision(user, registration.ExpectedRevision);
        var expectedRevision = user.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.PersonnelDisableHold = false;
        Stamp(user, registration.ActorId, now);
        await ReplaceUserOrThrowConflictAsync(user, expectedRevision, cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            user.EID,
            IdentitySecurityEventTypes.PersonnelDisableHoldReleased,
            "人员恢复并解除停用阻断",
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(user, changed: true);
    }

    private static IdentityLifecycleRegistrationResult Result(User user, bool changed) => new()
    {
        Account = IdentityAccountApplicationService.ToSnapshot(user),
        Changed = changed
    };

    private async ValueTask<User> GetRequiredUserAsync(string userEid, CancellationToken cancellationToken) =>
        await _repository.FindUserAsync(userEid, cancellationToken)
        ?? throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);

    private static void EnsureAvailableOrdinaryUser(User user)
    {
        if (user.Status == IdentityAccountStatus.Deleted)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.DeletedAccount,
                IdentityAccountApplicationService.ToSnapshot(user));
        }

        if (user.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.LocalAdministratorProtected,
                IdentityAccountApplicationService.ToSnapshot(user));
        }
    }

    private static void EnsureRevision(User user, long expectedRevision)
    {
        if (user.Revision != expectedRevision)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.RevisionConflict,
                IdentityAccountApplicationService.ToSnapshot(user));
        }
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
            latest is null ? null : IdentityAccountApplicationService.ToSnapshot(latest));
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 500)
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidReason);
        }

        return normalized;
    }

    private static void ValidateCaller(string actorId, string traceId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(traceId))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidActor);
        }
    }

    private static void Stamp(User user, string actorId, DateTime occurredAt)
    {
        user.Revision++;
        user.UpdatedBy = actorId;
        user.UpdatedAt = occurredAt;
    }

    private IdentitySecurityEvent CreateEvent(
        string targetUserEid,
        string eventType,
        string reason,
        string actorId,
        string traceId,
        DateTime occurredAt) => new()
    {
        EID = _objectIdGenerator.NewId(nameof(IdentitySecurityEvent)),
        TargetUserEID = targetUserEid,
        EventType = eventType,
        Reason = reason,
        OccurredAt = occurredAt,
        TraceId = traceId,
        ActorKind = IdentitySecurityActorKind.User,
        ActorUserEID = actorId,
        CreatedBy = actorId,
        CreatedAt = occurredAt,
        UpdatedBy = actorId,
        UpdatedAt = occurredAt
    };
}
