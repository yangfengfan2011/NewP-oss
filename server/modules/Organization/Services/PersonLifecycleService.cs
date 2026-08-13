using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonLifecycleService(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IAuthenticationSessionRegistry sessionRegistry,
    IPlatformTransactionExecutor transactionExecutor,
    TimeProvider timeProvider) : IPersonLifecycleService
{
    public async ValueTask<PersonLifecycleDto> SetStatusAsync(
        string personEid,
        SetPersonLifecycleStatusRequest request,
        PersonLifecycleActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        if (string.IsNullOrWhiteSpace(personEid) || request.ExpectedRevision < 0)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidRequest);
        }

        var reason = NormalizeReason(request.Reason);
        var initialPerson = await GetRequiredPersonAsync(personEid, cancellationToken);
        var initialAccount = await FindBoundAccountAsync(initialPerson, cancellationToken);
        if (IsConverged(initialPerson, initialAccount, request.IsEnabled))
        {
            return ToDto(initialPerson, initialAccount);
        }

        if (initialPerson.Revision != request.ExpectedRevision)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.Conflict, ToDto(initialPerson, initialAccount));
        }

        var changedAt = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            // Organization 负责人员生命周期编排；Person、Identity 与 Authentication 都只在此共享事务内登记变化，禁止参与者自行 SaveChanges。
            return await transactionExecutor.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    var person = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                    var account = await FindBoundAccountAsync(person, transactionCancellationToken);
                    if (IsConverged(person, account, request.IsEnabled))
                    {
                        return ToDto(person, account);
                    }

                    if (person.Revision != request.ExpectedRevision)
                    {
                        throw new PersonLifecycleException(PersonLifecycleFailureCodes.Conflict, ToDto(person, account));
                    }

                    var personChanged = await repository.TrySetEnablementAsync(
                        personEid,
                        request.IsEnabled,
                        request.ExpectedRevision,
                        actor.ActorId,
                        changedAt,
                        transactionCancellationToken);
                    if (!personChanged)
                    {
                        var latest = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                        throw new PersonLifecycleException(PersonLifecycleFailureCodes.Conflict, ToDto(latest, account));
                    }

                    IdentityAccountSnapshot? changedAccount = account;
                    if (person.AccountEid is not null)
                    {
                        if (request.IsEnabled)
                        {
                            changedAccount = (await identityParticipant.RegisterPersonnelDisableHoldReleaseAsync(
                                new PersonnelDisableHoldReleaseRegistration
                                {
                                    AccountEid = person.AccountEid,
                                    ExpectedRevision = account!.Revision,
                                    ActorId = actor.ActorId,
                                    TraceId = actor.TraceId
                                },
                                transactionCancellationToken)).Account;
                        }
                        else
                        {
                            var identityResult = await identityParticipant.RegisterPersonnelDisableAsync(
                                new PersonnelAccountDisableRegistration
                                {
                                    AccountEid = person.AccountEid,
                                    ExpectedRevision = account!.Revision,
                                    Reason = reason,
                                    ActorId = actor.ActorId,
                                    TraceId = actor.TraceId
                                },
                                transactionCancellationToken);
                            changedAccount = identityResult.Account;
                            if (identityResult.Changed)
                            {
                                _ = await sessionRegistry.RevokeAllAsync(
                                    new AccountAuthenticationSessionsRevocation
                                    {
                                        AccountEid = person.AccountEid,
                                        Reason = reason,
                                        ActorId = actor.ActorId
                                    },
                                    transactionCancellationToken);
                            }
                        }
                    }

                    return new PersonLifecycleDto(
                        person.PersonEid,
                        request.IsEnabled,
                        person.Revision + 1,
                        person.AccountEid,
                        ToAccount(changedAccount));
                },
                cancellationToken);
        }
        catch (PersonLifecycleException)
        {
            throw;
        }
        catch (IdentityLifecycleException exception) when (exception.FailureCode == IdentityLifecycleFailureCodes.RevisionConflict)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.Conflict, exception.LatestAccount, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable, innerException: exception);
        }
    }

    private async ValueTask<PersonUserBindingState> GetRequiredPersonAsync(string personEid, CancellationToken cancellationToken) =>
        await repository.FindAsync(personEid, cancellationToken)
        ?? throw new PersonLifecycleException(PersonLifecycleFailureCodes.PersonNotFound);

    private async ValueTask<IdentityAccountSnapshot?> FindBoundAccountAsync(
        PersonUserBindingState person,
        CancellationToken cancellationToken)
    {
        if (person.AccountEid is null)
        {
            return null;
        }

        try
        {
            return await accountDirectory.FindAsync(person.AccountEid, cancellationToken)
                ?? throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable);
        }
        catch (PersonLifecycleException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable, innerException: exception);
        }
    }

    private static bool IsConverged(
        PersonUserBindingState person,
        IdentityAccountSnapshot? account,
        bool targetEnabled)
    {
        if (person.IsEnabled != targetEnabled)
        {
            return false;
        }

        return person.AccountEid is null || (targetEnabled
            ? account is { PersonnelDisableHold: false }
            : account is { Status: IdentityAccountStatus.Disabled, PersonnelDisableHold: true });
    }

    private static void ValidateActor(PersonLifecycleActor actor)
    {
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.TraceId))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
        }
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 500)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidReason);
        }

        return normalized;
    }

    private static PersonLifecycleDto ToDto(PersonUserBindingState person, IdentityAccountSnapshot? account) =>
        new(person.PersonEid, person.IsEnabled, person.Revision, person.AccountEid, ToAccount(account));

    private static PersonLifecycleAccountDto? ToAccount(IdentityAccountSnapshot? account) => account is null
        ? null
        : new(account.AccountEid, account.Status.ToString(), account.Revision, account.PersonnelDisableHold);
}
