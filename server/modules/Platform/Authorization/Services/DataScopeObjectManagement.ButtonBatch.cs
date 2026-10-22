using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> WithinBoundaryAsync(DataScopeBatchSaveInput input, string actionKey,
        Func<DataScopeBatchSaveState, CancellationToken, Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>> execute, CancellationToken token)
    {
        var entered = false;
        var result = await WriteAsync(input.Targets, (ordered, cancellation) => PrepareBatchAsync(ordered, input.Configuration, cancellation), token,
            async (targets, save, cancellation) =>
            {
                var targetSet = await ReadButtonTargetsAsync(targets, cancellation);
                DataScopeObjectResult<DataScopeBatchAuthorizationReceipt>? committed = null;
                var active = true;
                var calls = 0;
                var state = new BatchSaveState(targetSet, async inner =>
                {
                    if (!active || Interlocked.Increment(ref calls) != 1)
                        throw new InvalidOperationException("DATA_SCOPE_BATCH_SAVE_BOUNDARY_REQUIRED");
                    committed = await save(inner);
                    return BatchResult(committed);
                });
                entered = true;
                try
                {
                    await execute(state, cancellation);
                    return committed ?? throw new InvalidOperationException("DATA_SCOPE_BATCH_SAVE_REQUIRED");
                }
                finally { active = false; }
            });
        if (!entered) ThrowBatchBoundaryFailure(result.Status);
        return BatchResult(result);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeBatchSaveState state) => state.Target;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> ExecuteAsync(DataScopeBatchSaveState state, CancellationToken token) =>
        state is BatchSaveState owned ? owned.Save(token) : throw new InvalidOperationException("DATA_SCOPE_BATCH_SAVE_STATE_INVALID");

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> WithinBoundaryAsync(DataScopeBatchReadInput input, string actionKey,
        Func<DataScopeBatchReadState, CancellationToken, Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>> execute, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        return await transactions.ExecuteAsync<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>(async cancellation =>
        {
            var ordered = OrderTargets(input.Targets);
            var status = await LockAndCheckTargetsAsync(ordered, cancellation);
            if (status != DataScopeObjectStatus.Succeeded) ThrowBatchBoundaryFailure(status);
            return await execute(new BatchReadState(ordered, await ReadButtonTargetsAsync(ordered, cancellation)), cancellation);
        }, token);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeBatchReadState state) => state.Target;

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> ExecuteAsync(DataScopeBatchReadState state, CancellationToken token)
    {
        if (state is not BatchReadState owned) throw new InvalidOperationException("DATA_SCOPE_BATCH_READ_STATE_INVALID");
        var configurations = new List<DataScopeObjectConfiguration>();
        foreach (var target in owned.Targets) configurations.Add(await grants.GetAsync(target.ResourceKey, target.ObjectId, token));
        return new(DataScopeAuthorizationStatus.Succeeded, configurations);
    }

    // 全部锁内使用正式资源模型和IdField读取事实，不要求专用资源暴露标准CRUD。
    private async Task<ButtonTargetSet> ReadButtonTargetsAsync(IReadOnlyList<DataScopeAuthorizationTarget> targets, CancellationToken token)
    {
        var values = new List<ButtonObjectTarget>();
        foreach (var target in targets)
        {
            var resource = catalog.Resolve(target.ResourceKey);
            var before = await catalog.ReadObjectAsync(target.ResourceKey, target.ObjectId, token)
                ?? throw new ActionDataAccessException(404, "StandardCrud.NotFound");
            values.Add(new ButtonExecutionTarget(target.ResourceKey, resource.ModelName, target.ObjectId, before));
        }
        return new(values.ToArray());
    }

    // 保留原批量完整回执及错误，不在成功后读取可能已失去资格的直接配置。
    private static DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt> BatchResult(DataScopeObjectResult<DataScopeBatchAuthorizationReceipt> result) =>
        new(AuthorizationStatus(result.Status), result.Value, result.ValidationError is null ? null : Error(result.ValidationError));

    // 自动执行尚未取得状态时也必须明确拒绝，不能把空集合当作无要求。
    private static void ThrowBatchBoundaryFailure(DataScopeObjectStatus status) =>
        throw new ActionDataAccessException(status == DataScopeObjectStatus.NotFound ? 404 : status == DataScopeObjectStatus.Forbidden ? 403 : 400,
            status == DataScopeObjectStatus.NotFound ? "StandardCrud.NotFound" : status == DataScopeObjectStatus.Forbidden ? "DataScope.ActionDenied" : "DataScope.InvalidConfiguration");

    private sealed record BatchSaveState(ButtonTargetSet Target,
        Func<CancellationToken, Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>> Save) : DataScopeBatchSaveState(Target);
    private sealed record BatchReadState(IReadOnlyList<DataScopeAuthorizationTarget> Targets, ButtonTargetSet Target) : DataScopeBatchReadState(Target);
}
