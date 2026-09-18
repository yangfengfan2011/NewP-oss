using System.Text;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonUserBindingService(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IPlatformTransactionExecutor transactionExecutor,
    IEntityAuditValueProvider auditValues,
    Func<IObjectOperationExecutor>? resolveObjectOperations = null,
    PersonObjectActionInvocationContext? invocations = null) : IPersonUserBindingService
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
        if (actor is null)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidActor);
        }
        if (resolveObjectOperations is null || invocations is null)
        {
            return (await ExecuteCoreAsync(personEid, request, actor, cancellationToken)).Dto;
        }

        using var invocation = invocations.Begin(actor);
        try
        {
            var result = await resolveObjectOperations().ExecuteAsync(
                PersonObjectBehavior.BehaviorDefinition.ObjectKey,
                PersonObjectBehavior.BindUserAction,
                personEid,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["AccountEid"] = request.AccountEid,
                    ["ExpectedRevision"] = request.ExpectedRevision,
                    ["Reason"] = request.Reason
                },
                cancellationToken);
            return ReadResult(result);
        }
        catch (PlatformCacheUnavailableException)
        {
            throw;
        }
        catch (PersonUserBindingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <summary>在对象行为核心中校验最终输入并登记永久 Person/Identity 绑定事实。</summary>
    internal async ValueTask<PersonUserBindingActionResult> ExecuteCoreAsync(
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
            return new(
                await EnsureExistingBindingAsync(initialPerson, request.AccountEid, cancellationToken),
                initialPerson);
        }

        EnsurePersonCanStartBinding(initialPerson);
        var account = await FindAccountAsync(request.AccountEid, cancellationToken);
        EnsureCandidate(account, request.ExpectedRevision);
        async ValueTask<PersonUserBindingActionResult> ExecuteAsync(CancellationToken transactionCancellationToken)
        {
            var person = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
            if (person.AccountEid is not null)
            {
                return new(EnsureSameBindingOrThrow(person, account.AccountEid), person);
            }

            EnsurePersonCanStartBinding(person);
            var audit = ResolveAuditValues(person, actor.ActorId);
            var bound = await repository.TryBindAsync(
                personEid,
                account.AccountEid,
                reason,
                audit,
                transactionCancellationToken);
            if (!bound)
            {
                var latest = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                if (latest.AccountEid is not null)
                {
                    return new(EnsureSameBindingOrThrow(latest, account.AccountEid), latest);
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
                        AccountEid = account.AccountEid,
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

            var changedPerson = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
            return new(
                new PersonUserBindingDto(
                    changedPerson.PersonEid,
                    changedPerson.AccountEid!,
                    changedPerson.BoundAt!.Value,
                    changedPerson.BoundBy!,
                    changedPerson.BindingReason!,
                    ToAccount(identityResult.Account)),
                changedPerson);
        }

        return transactionExecutor.IsActive
            ? await ExecuteAsync(cancellationToken)
            : await transactionExecutor.ExecuteAsync(["Person"], ExecuteAsync, cancellationToken);
    }

    // 已知相同标识仍可直接幂等返回；其他输入先经 Identity 解析，再按实际标识判断永久绑定目标。
    private async ValueTask<PersonUserBindingDto> EnsureExistingBindingAsync(PersonUserBindingState person,
        string requestedAccountEid, CancellationToken cancellationToken)
    {
        if (string.Equals(person.AccountEid, requestedAccountEid, StringComparison.Ordinal))
            return EnsureSameBindingOrThrow(person, requestedAccountEid);

        IdentityAccountSnapshot account;
        try { account = await FindAccountAsync(requestedAccountEid, cancellationToken); }
        catch (PersonUserBindingException exception) when (exception.FailureCode == PersonUserBindingFailureCodes.UserNotAvailable)
        {
            // 不存在的目标仍是改绑冲突，不改变已绑定人员的既有失败契约。
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.PersonAlreadyBound, person);
        }
        return EnsureSameBindingOrThrow(person, account.AccountEid);
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

    // 直接 DML 使用公共可信审计规则；既有 actor 参数只验证同一服务端主体，不能覆盖最终审计值。
    private EntityAuditValues ResolveAuditValues(PersonUserBindingState person, string actorId)
    {
        if (string.IsNullOrWhiteSpace(person.CreatedBy) || person.CreatedAt is null)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.IdentityUnavailable);
        }
        var values = auditValues.ForUpdate(person.CreatedBy, person.CreatedAt.Value);
        if (!string.Equals(values.UpdatedBy, actorId, StringComparison.Ordinal))
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidActor);
        }
        return values;
    }

    // 对象入口保留原 DTO；扩展拒绝和领域拒绝都按原 HTTP code/status 返回。
    private static PersonUserBindingDto ReadResult(StandardCrudMutationResult result)
    {
        if (result.Succeeded && result.Detail?.Fields.GetValueOrDefault("Result") is PersonUserBindingDto dto)
        {
            return dto;
        }
        var error = result.Error ?? throw new InvalidOperationException("Person binding behavior returned no result.");
        ThrowCacheFailure(error);
        error.Args.TryGetValue("current", out var current);
        throw new PersonUserBindingException(error.Code, current, statusCode: error.StatusCode);
    }

    // 对象执行器统一表达提交后发布失败；专用 HTTP 边界恢复原异常类型与稳定 code。
    private static void ThrowCacheFailure(StandardCrudError error)
    {
        if (error.Code == "Persistence.CommittedCacheUnavailable")
        {
            throw new PlatformCacheUnavailableException("Organization", databaseCommitted: true);
        }
        if (error.Code == "Persistence.CommitOutcomeUnknown")
        {
            throw new PlatformCacheUnavailableException("Organization", commitOutcomeUnknown: true);
        }
        if (error.Code == "Persistence.CacheUnavailable")
        {
            throw new PlatformCacheUnavailableException("Organization");
        }
    }
}

internal sealed record PersonUserBindingActionResult(
    PersonUserBindingDto Dto,
    PersonUserBindingState Person);
