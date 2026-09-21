using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

// 人员生命周期由业务服务拥有，并复用既有核心与平台事务协调。
public sealed class PersonLifecycleService : IPersonLifecycleService
{
    private readonly PersonLifecycleActionCore _core;
    private readonly IPlatformTransactionExecutor _transactionExecutor;

    // 服务入口统一事务协调和失败映射，内部核心只登记生命周期规则与跨模块变化。
    internal PersonLifecycleService(
        PersonLifecycleActionCore core,
        IPlatformTransactionExecutor transactionExecutor)
    {
        _core = core;
        _transactionExecutor = transactionExecutor;
    }

    /// <summary>
    /// 在共享事务中同步人员状态及其已绑定账号的生命周期事实。
    /// </summary>
    public async ValueTask<PersonLifecycleDto> SetStatusAsync(
        string personEid,
        SetPersonLifecycleStatusRequest request,
        PersonLifecycleActor actor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _transactionExecutor.ExecuteAsync(
                ["Person"],
                token => _core.ExecuteAsync(personEid, request, actor, token),
                cancellationToken);
        }
        catch (PlatformCacheUnavailableException)
        {
            // 保留提交已完成或结果未知的事实，不能引导调用者重放。
            throw;
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
}
