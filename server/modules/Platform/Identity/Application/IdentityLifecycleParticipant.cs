using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class IdentityLifecycleParticipant : IIdentityLifecycleParticipant
{
    private readonly IIdentityRepository _repository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly TimeProvider _timeProvider;

    public IdentityLifecycleParticipant(
        IIdentityRepository repository,
        IPlatformObjectIdGenerator objectIdGenerator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _objectIdGenerator = objectIdGenerator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(
        PersonnelBindingRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RegisterPersonnelBindingCoreAsync(registration, cancellationToken);
        }
        catch (IdentityApplicationException exception)
        {
            throw new IdentityLifecycleException(
                exception.FailureCode,
                exception.LatestAccount,
                exception);
        }
    }

    private async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingCoreAsync(
        PersonnelBindingRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        if (string.IsNullOrWhiteSpace(registration.PersonEid))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidPersonReference);
        }

        var reason = IdentityInputNormalizer.NormalizeReason(registration.Reason);
        var account = await GetRequiredAccountAsync(registration.AccountEid, cancellationToken);
        EnsureAvailableOrdinaryAccount(account);
        if (account.IsPersonnelBound)
        {
            return Result(account, changed: false);
        }

        EnsureRevision(account, registration.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.IsPersonnelBound = true;
        Stamp(account, registration.ActorId, now);
        await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            account.EID,
            IdentitySecurityEventTypes.PersonnelBound,
            reason,
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(account, changed: true);
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(
        PersonnelAccountDisableRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RegisterPersonnelDisableCoreAsync(registration, cancellationToken);
        }
        catch (IdentityApplicationException exception)
        {
            throw new IdentityLifecycleException(
                exception.FailureCode,
                exception.LatestAccount,
                exception);
        }
    }

    private async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableCoreAsync(
        PersonnelAccountDisableRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        var reason = IdentityInputNormalizer.NormalizeReason(registration.Reason);
        var account = await GetRequiredAccountAsync(registration.AccountEid, cancellationToken);
        EnsureAvailableOrdinaryAccount(account);
        if (account.Status == IdentityAccountStatus.Disabled && account.PersonnelDisableHold)
        {
            return Result(account, changed: false);
        }

        EnsureRevision(account, registration.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.Status = IdentityAccountStatus.Disabled;
        account.PersonnelDisableHold = true;
        Stamp(account, registration.ActorId, now);
        await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            account.EID,
            IdentitySecurityEventTypes.PersonnelDisabled,
            reason,
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(account, changed: true);
    }

    public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(
        PersonnelDisableHoldReleaseRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RegisterPersonnelDisableHoldReleaseCoreAsync(registration, cancellationToken);
        }
        catch (IdentityApplicationException exception)
        {
            throw new IdentityLifecycleException(
                exception.FailureCode,
                exception.LatestAccount,
                exception);
        }
    }

    private async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseCoreAsync(
        PersonnelDisableHoldReleaseRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateCaller(registration.ActorId, registration.TraceId);
        var account = await GetRequiredAccountAsync(registration.AccountEid, cancellationToken);
        EnsureAvailableOrdinaryAccount(account);
        if (!account.PersonnelDisableHold)
        {
            return Result(account, changed: false);
        }

        EnsureRevision(account, registration.ExpectedRevision);
        var expectedRevision = account.Revision;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.PersonnelDisableHold = false;
        Stamp(account, registration.ActorId, now);
        await ReplaceAccountOrThrowConflictAsync(account, expectedRevision, cancellationToken);
        _repository.AddSecurityEvent(CreateEvent(
            account.EID,
            IdentitySecurityEventTypes.PersonnelDisableHoldReleased,
            "人员恢复并解除停用阻断",
            registration.ActorId,
            registration.TraceId,
            now));
        return Result(account, changed: true);
    }

    private static IdentityLifecycleRegistrationResult Result(IdentityAccount account, bool changed) => new()
    {
        Account = IdentityAccountApplicationService.ToSnapshot(account),
        Changed = changed
    };

    private async ValueTask<IdentityAccount> GetRequiredAccountAsync(string accountEid, CancellationToken cancellationToken) =>
        await _repository.FindAccountAsync(accountEid, cancellationToken)
        ?? throw new IdentityApplicationException(IdentityFailureCodes.AccountNotFound);

    private static void EnsureAvailableOrdinaryAccount(IdentityAccount account)
    {
        if (account.Status == IdentityAccountStatus.Deleted)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.DeletedAccount,
                IdentityAccountApplicationService.ToSnapshot(account));
        }

        if (account.IsLocalAdministrator)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.LocalAdministratorProtected,
                IdentityAccountApplicationService.ToSnapshot(account));
        }
    }

    private static void EnsureRevision(IdentityAccount account, long expectedRevision)
    {
        if (account.Revision != expectedRevision)
        {
            throw new IdentityApplicationException(
                IdentityFailureCodes.RevisionConflict,
                IdentityAccountApplicationService.ToSnapshot(account));
        }
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
            latest is null ? null : IdentityAccountApplicationService.ToSnapshot(latest));
    }

    private static void ValidateCaller(string actorId, string traceId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(traceId))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidActor);
        }
    }

    private static void Stamp(IdentityAccount account, string actorId, DateTime occurredAt)
    {
        account.Revision++;
        account.UpdatedBy = actorId;
        account.UpdatedAt = occurredAt;
    }

    private IdentitySecurityEvent CreateEvent(
        string targetAccountEid,
        string eventType,
        string reason,
        string actorId,
        string traceId,
        DateTime occurredAt) => new()
    {
        EID = _objectIdGenerator.NewId(nameof(IdentitySecurityEvent)),
        TargetAccountEID = targetAccountEid,
        EventType = eventType,
        Reason = reason,
        OccurredAt = occurredAt,
        TraceId = traceId,
        ActorKind = IdentitySecurityActorKind.IdentityAccount,
        ActorAccountEID = actorId,
        CreatedBy = actorId,
        CreatedAt = occurredAt,
        UpdatedBy = actorId,
        UpdatedAt = occurredAt
    };
}
