using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult> WithinBoundaryAsync(DataScopeObjectSaveInput input, string actionKey,
        Func<DataScopeObjectSaveState, CancellationToken, Task<DataScopeAuthorizationResult>> execute, CancellationToken token)
    {
        var entered = false;
        var result = await SaveConfigurationAsync(input.ResourceKey, input.ObjectId, input.Configuration, token,
            async (targets, save, cancellation) =>
            {
                var target = targets.Single();
                var resource = catalog.Resolve(target.ResourceKey);
                var before = await catalog.ReadObjectAsync(target.ResourceKey, target.ObjectId, cancellation)
                    ?? throw new ActionDataAccessException(404, "StandardCrud.NotFound");
                DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>? committed = null;
                var active = true;
                var calls = 0;
                var state = new ObjectSaveState(new(target.ResourceKey, resource.ModelName, target.ObjectId, before), async inner =>
                {
                    if (!active || Interlocked.Increment(ref calls) != 1)
                        throw new InvalidOperationException("DATA_SCOPE_OBJECT_SAVE_BOUNDARY_REQUIRED");
                    committed = await save(inner);
                    return SaveResult(committed);
                });
                entered = true;
                try
                {
                    await execute(state, cancellation);
                    return committed ?? throw new InvalidOperationException("DATA_SCOPE_OBJECT_SAVE_REQUIRED");
                }
                finally { active = false; }
            });
        if (!entered) throw new ActionDataAccessException(result.Status == DataScopeObjectStatus.NotFound ? 404 : 403,
            result.Status == DataScopeObjectStatus.NotFound ? "StandardCrud.NotFound" : "DataScope.ActionDenied");
        return SaveResult(result);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectSaveState state) => state.Target;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult> ExecuteAsync(DataScopeObjectSaveState state, CancellationToken token) =>
        state is ObjectSaveState owned ? owned.Save(token) : throw new InvalidOperationException("DATA_SCOPE_OBJECT_SAVE_STATE_INVALID");

    // 手工授权业务在原 ACL 锁内主动重验管理资格，不以按钮先前允许代替当前对象事实。
    private async Task<DataScopeObjectEligibility> CheckManagementActionAsync(string resourceKey, string objectId, CancellationToken token)
    {
        var decision = await (objectActions ?? throw new InvalidOperationException("DATA_SCOPE_OBJECT_ACTIONS_REQUIRED"))
            .CheckAsync(catalog.Resolve(resourceKey).ModelName, objectId, ["read", "authorize"], token);
        return decision switch { DataExecutionDecision.Allowed => DataScopeObjectEligibility.Allowed,
            DataExecutionDecision.NotFound => DataScopeObjectEligibility.NotFound, _ => DataScopeObjectEligibility.Forbidden };
    }

    // 保留既有配置错误事实与安全成功，不对保存后的操作者追加读取。
    private static DataScopeAuthorizationResult SaveResult(DataScopeObjectResult result) =>
        new(AuthorizationStatus(result.Status), result.ValidationError is null ? null : Error(result.ValidationError));

    private sealed record ObjectSaveState(ButtonExecutionTarget Target, Func<CancellationToken, Task<DataScopeAuthorizationResult>> Save)
        : DataScopeObjectSaveState(Target);
}
