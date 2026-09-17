using System.Text;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonUserBindingService(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IPlatformTransactionExecutor transactionExecutor,
    TimeProvider timeProvider) : IPersonUserBindingService
{
    public async ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
        string personEid,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var person = await GetRequiredPersonAsync(personEid, cancellationToken);
        EnsurePersonCanStartBinding(person);

        IReadOnlyList<IdentityAccountSnapshot> accounts;
        try
        {
            accounts = await accountDirectory.SearchAsync(
                new IdentityAccountSearch
                {
                    Query = string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                    IsPersonnelBound = false,
                    Limit = 20
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PersonUserBindingException(
                PersonUserBindingFailureCodes.IdentityUnavailable,
                innerException: exception);
        }

        return accounts
            .Where(static account => account.Status is IdentityAccountStatus.Enabled or IdentityAccountStatus.Disabled)
            .Where(static account => !account.IsPersonnelBound && !account.IsLocalAdministrator)
            .Take(20)
            .Select(static account => new PersonUserBindingCandidateDto(
                account.AccountEid,
                account.ECODE,
                account.ENAME,
                account.Status.ToString(),
                account.Revision))
            .ToArray();
    }

    /// <summary>在组织写协调和共享事务内永久绑定人员及账号，提交后统一发布完整人员结果。</summary>
    public async ValueTask<PersonUserBindingDto> BindAsync(
        string personEid,
        BindPersonUserRequest request,
        PersonUserBindingActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        if (string.IsNullOrWhiteSpace(personEid) || string.IsNullOrWhiteSpace(request.AccountEid))
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidRequest);
        }

        var reason = NormalizeReason(request.Reason);
        var initialPerson = await GetRequiredPersonAsync(personEid, cancellationToken);
        if (initialPerson.AccountEid is not null)
        {
            return EnsureSameBindingOrThrow(initialPerson, request.AccountEid);
        }

        EnsurePersonCanStartBinding(initialPerson);
        var account = await FindAccountAsync(request.AccountEid, cancellationToken);
        EnsureCandidate(account, request.ExpectedRevision);
        var boundAt = timeProvider.GetUtcNow().UtcDateTime;

        return await transactionExecutor.ExecuteAsync(
            ["Person"],
            async transactionCancellationToken =>
            {
                var person = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                if (person.AccountEid is not null)
                {
                    return EnsureSameBindingOrThrow(person, request.AccountEid);
                }

                EnsurePersonCanStartBinding(person);
                var bound = await repository.TryBindAsync(
                    personEid,
                    request.AccountEid,
                    boundAt,
                    actor.ActorId,
                    reason,
                    transactionCancellationToken);
                if (!bound)
                {
                    var latest = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                    if (latest.AccountEid is not null)
                    {
                        return EnsureSameBindingOrThrow(latest, request.AccountEid);
                    }

                    EnsurePersonCanStartBinding(latest);
                    throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonAlreadyBound);
                }

                IdentityLifecycleRegistrationResult identityResult;
                try
                {
                    identityResult = await identityParticipant.RegisterPersonnelBindingAsync(
                        new PersonnelBindingRegistration
                        {
                            PersonEid = personEid,
                            AccountEid = request.AccountEid,
                            ExpectedRevision = request.ExpectedRevision,
                            Reason = reason,
                            ActorId = actor.ActorId,
                            TraceId = actor.TraceId
                        },
                        transactionCancellationToken);
                }
                catch (IdentityLifecycleException exception)
                {
                    throw MapIdentityFailure(exception);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new PersonUserBindingException(
                        PersonUserBindingFailureCodes.IdentityUnavailable,
                        innerException: exception);
                }

                // Person 此前尚未绑定，因此 Identity 的无变化只能表示账号已被其他人员抢先绑定。
                if (!identityResult.Changed)
                {
                    throw new PersonUserBindingException(
                        PersonUserBindingFailureCodes.UserAlreadyBound,
                        identityResult.Account);
                }

                return new PersonUserBindingDto(
                    personEid,
                    request.AccountEid,
                    boundAt,
                    actor.ActorId,
                    reason,
                    ToAccount(identityResult.Account));
            },
            cancellationToken);
    }

    private async ValueTask<IdentityAccountSnapshot> FindAccountAsync(
        string accountEid,
        CancellationToken cancellationToken)
    {
        try
        {
            return await accountDirectory.FindAsync(accountEid, cancellationToken)
                ?? throw new PersonUserBindingException(PersonUserBindingFailureCodes.UserNotAvailable);
        }
        catch (PersonUserBindingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PersonUserBindingException(
                PersonUserBindingFailureCodes.IdentityUnavailable,
                innerException: exception);
        }
    }

    private async ValueTask<PersonUserBindingState> GetRequiredPersonAsync(
        string personEid,
        CancellationToken cancellationToken) =>
        await repository.FindAsync(personEid, cancellationToken)
        ?? throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonNotFound);

    private static void EnsurePersonCanStartBinding(PersonUserBindingState person)
    {
        if (person.AccountEid is not null)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonAlreadyBound, person);
        }

        if (!person.IsEnabled)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonDisabled, person);
        }
    }

    private static void EnsureCandidate(IdentityAccountSnapshot account, long expectedRevision)
    {
        if (account.Status == IdentityAccountStatus.Deleted)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.UserNotAvailable, account);
        }

        if (account.IsLocalAdministrator)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.LocalAdministratorProtected, account);
        }

        if (account.IsPersonnelBound)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.UserAlreadyBound, account);
        }

        if (account.Revision != expectedRevision)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.RevisionConflict, account);
        }
    }

    private static PersonUserBindingDto EnsureSameBindingOrThrow(
        PersonUserBindingState person,
        string requestedAccountEid)
    {
        if (!string.Equals(person.AccountEid, requestedAccountEid, StringComparison.Ordinal))
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonAlreadyBound, person);
        }

        return new PersonUserBindingDto(
            person.PersonEid,
            person.AccountEid!,
            person.BoundAt ?? default,
            person.BoundBy ?? string.Empty,
            person.BindingReason ?? string.Empty,
            Account: null);
    }

    private static void ValidateActor(PersonUserBindingActor actor)
    {
        if (actor is null
            || string.IsNullOrWhiteSpace(actor.ActorId)
            || string.IsNullOrWhiteSpace(actor.TraceId))
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidActor);
        }
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 500)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidReason);
        }

        return normalized;
    }

    private static PersonUserBindingAccountDto ToAccount(IdentityAccountSnapshot account) => new(
        account.AccountEid,
        account.ECODE,
        account.ENAME,
        account.Status.ToString(),
        account.Revision);

    private static PersonUserBindingException MapIdentityFailure(IdentityLifecycleException exception)
    {
        return exception.FailureCode switch
        {
            IdentityLifecycleFailureCodes.RevisionConflict => new PersonUserBindingException(
                PersonUserBindingFailureCodes.RevisionConflict,
                exception.LatestAccount,
                exception),
            IdentityLifecycleFailureCodes.AccountNotFound or IdentityLifecycleFailureCodes.DeletedAccount => new PersonUserBindingException(
                PersonUserBindingFailureCodes.UserNotAvailable,
                exception.LatestAccount,
                exception),
            IdentityLifecycleFailureCodes.LocalAdministratorProtected => new PersonUserBindingException(
                PersonUserBindingFailureCodes.LocalAdministratorProtected,
                exception.LatestAccount,
                exception),
            _ => new PersonUserBindingException(
                PersonUserBindingFailureCodes.IdentityUnavailable,
                innerException: exception)
        };
    }
}
