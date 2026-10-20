using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>整批输入只携带真实出现位置与业务配置，规范对象由owner在图锁内取得。</summary>
public sealed record WorkspaceAuthorizationBatchSaveInput(string ActorId, IReadOnlyList<WorkspaceBatchPermissionSource> Sources,
    DataScopeBatchConfiguration Configuration);

/// <summary>核对当前完整来源，不把原模板或结果当作提交资格。</summary>
public sealed record WorkspaceAuthorizationBatchReadInput(string ActorId, IReadOnlyList<WorkspaceBatchPermissionSource> Sources);

/// <summary>原图保护贯穿规范集合解析、共享ACL重验及全量保存。</summary>
public sealed class WorkspaceAuthorizationBatchSaveHandler(WorkspaceContents contents, IDataScopeBatchSaveHandler authorization)
    : IButtonBusinessHandler<WorkspaceAuthorizationBatchSaveInput, DataScopeBatchSaveState, DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> WithinBoundaryAsync(WorkspaceAuthorizationBatchSaveInput input, string actionKey,
        Func<DataScopeBatchSaveState, CancellationToken, Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>> execute, CancellationToken token) =>
        contents.WithinBatchPermissionBoundaryAsync(input.ActorId, input.Sources, (targets, cancellation) =>
            authorization.WithinBoundaryAsync(new(targets, input.Configuration), actionKey, execute, cancellation), token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeBatchSaveState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> ExecuteAsync(DataScopeBatchSaveState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}

/// <summary>核对仍需完整集合当前管理资格，失败不返回部分直接项。</summary>
public sealed class WorkspaceAuthorizationBatchReadHandler(WorkspaceContents contents, IDataScopeBatchReadHandler authorization)
    : IButtonBusinessHandler<WorkspaceAuthorizationBatchReadInput, DataScopeBatchReadState, DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> WithinBoundaryAsync(WorkspaceAuthorizationBatchReadInput input, string actionKey,
        Func<DataScopeBatchReadState, CancellationToken, Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>> execute, CancellationToken token) =>
        contents.WithinBatchPermissionBoundaryAsync(input.ActorId, input.Sources, (targets, cancellation) =>
            authorization.WithinBoundaryAsync(new(targets), actionKey, execute, cancellation), token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeBatchReadState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> ExecuteAsync(DataScopeBatchReadState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}
