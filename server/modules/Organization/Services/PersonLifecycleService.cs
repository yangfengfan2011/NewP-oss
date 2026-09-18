using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonLifecycleService(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IAuthenticationSessionRegistry sessionRegistry,
    IPlatformTransactionExecutor transactionExecutor,
    IEntityAuditValueProvider auditValues,
    Func<IObjectOperationExecutor>? resolveObjectOperations = null,
    PersonObjectActionInvocationContext? invocations = null) : IPersonLifecycleService
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
        if (actor is null)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
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
                request.IsEnabled ? PersonObjectBehavior.EnableAction : PersonObjectBehavior.DisableAction,
                personEid,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Reason"] = request.Reason },
                cancellationToken);
            return ReadResult(result);
        }
        catch (PlatformCacheUnavailableException)
        {
            throw;
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

    /// <summary>在对象行为核心中校验最终输入并同步 Person、Identity 与 Authentication 事实。</summary>
    internal async ValueTask<PersonLifecycleActionResult> ExecuteCoreAsync(
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
            return new(ToDto(initialPerson, initialAccount), initialPerson);
        }

        try
        {
            // Organization 负责人员生命周期编排；Person、Identity 与 Authentication 都只在此共享事务内登记变化，禁止参与者自行 SaveChanges。
            async ValueTask<PersonLifecycleActionResult> ExecuteAsync(CancellationToken transactionCancellationToken)
            {
                var person = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                var account = await FindBoundAccountAsync(person, transactionCancellationToken);
                if (IsConverged(person, account, request.IsEnabled))
                {
                    return new(ToDto(person, account), person);
                }

                var audit = ResolveAuditValues(person, actor.ActorId);
                await repository.SetEnablementAsync(
                    personEid,
                    request.IsEnabled,
                    audit,
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

                var changedPerson = await GetRequiredPersonAsync(personEid, transactionCancellationToken);
                return new(
                    new PersonLifecycleDto(
                        changedPerson.PersonEid,
                        request.IsEnabled,
                        changedPerson.AccountEid,
                        ToAccount(changedAccount)),
                    changedPerson);
            }

            return transactionExecutor.IsActive
                ? await ExecuteAsync(cancellationToken)
                : await transactionExecutor.ExecuteAsync(["Person"], ExecuteAsync, cancellationToken);
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

    // 直接 DML 与普通 EF 保存共用可信 actor、UTC 和更新时间下界；调用方 actor 只作为协议一致性校验。
    private EntityAuditValues ResolveAuditValues(PersonUserBindingState person, string actorId)
    {
        if (string.IsNullOrWhiteSpace(person.CreatedBy) || person.CreatedAt is null)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable);
        }
        var values = auditValues.ForUpdate(person.CreatedBy, person.CreatedAt.Value);
        if (!string.Equals(values.UpdatedBy, actorId, StringComparison.Ordinal))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
        }
        return values;
    }

    // 对象入口以不可变详情携带原 DTO；稳定失败仍投影回专用服务异常。
    private static PersonLifecycleDto ReadResult(StandardCrudMutationResult result)
    {
        if (result.Succeeded && result.Detail?.Fields.GetValueOrDefault("Result") is PersonLifecycleDto dto)
        {
            return dto;
        }
        var error = result.Error ?? throw new InvalidOperationException("Person lifecycle behavior returned no result.");
        ThrowCacheFailure(error);
        error.Args.TryGetValue("current", out var current);
        throw new PersonLifecycleException(error.Code, current, statusCode: error.StatusCode);
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

internal sealed record PersonLifecycleActionResult(
    PersonLifecycleDto Dto,
    PersonUserBindingState Person);
