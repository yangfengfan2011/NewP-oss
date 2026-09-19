using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

// 扩展可依赖旧服务，延迟解析执行器避免装配回环；写入口必须经过统一执行器。
public sealed class PersonLifecycleService(
    Func<IObjectOperationExecutor> resolveObjectOperations,
    PersonObjectActionInvocationContext invocations) : IPersonLifecycleService
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
