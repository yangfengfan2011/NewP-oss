using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonLifecycleService(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IAuthenticationSessionRegistry sessionRegistry,
    IPlatformTransactionExecutor transactionExecutor,
    TimeProvider timeProvider) : IPersonLifecycleService
{
    /// <summary>
    /// 在共享事务中同步人员状态及其已绑定账号的生命周期事实。
    /// </summary>
    public async ValueTask<PersonLifecycleDto> SetStatusAsync(
        string personEid,
        SetPersonLifecycleStatusRequest request,
        PersonLifecycleActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        if (string.IsNullOrWhiteSpace(personEid))
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

        var changedAt = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            // Organization 负责人员生命周期编排；Person、Identity 与 Authentication 都只在此共享事务内登记变化，禁止参与者自行 SaveChanges。
            return await transactionExecutor.ExecuteAsync(
                ["Person"],
                async transactionCancellationToken =>
                {
                    var person = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                    var account = await FindBoundAccountAsync(person, transactionCancellationToken);
                    if (IsConverged(person, account, request.IsEnabled))
                    {
                        return ToDto(person, account);
                    }

                    await repository.SetEnablementAsync(
                        personEid,
                        request.IsEnabled,
                        actor.ActorId,
                        changedAt,
                        transactionCancellationToken);

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
        catch (PlatformCacheUnavailableException)
        {
            // 已提交后的发布失败不能降格为可重试的依赖失败，HTTP 边界保留确定提交事实。
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
        new(person.PersonEid, person.IsEnabled, person.AccountEid, ToAccount(account));

    private static PersonLifecycleAccountDto? ToAccount(IdentityAccountSnapshot? account) => account is null
        ? null
        : new(account.AccountEid, account.Status.ToString(), account.Revision, account.PersonnelDisableHold);
}
