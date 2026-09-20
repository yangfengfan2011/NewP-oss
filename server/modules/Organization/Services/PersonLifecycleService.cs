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
                async token => (await _core.ExecuteAsync(personEid, request, actor, token)).Dto,
                cancellationToken);
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
}
