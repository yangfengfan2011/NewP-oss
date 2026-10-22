using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>管理读取固定真实业务操作；位置和LID每次重新解析，不接受客户端规范对象。</summary>
public sealed record WorkspaceAuthorizationReadInput(string ActorId, WorkspacePosition Position, string LinkId, string Operation,
    string? SubjectType = null, string? Query = null, int Page = 1, IReadOnlyList<string>? CurrentIds = null);

/// <summary>完整配置及非用户主体候选保持管理要求；纯查看仍走独立来源。</summary>
public sealed class WorkspaceAuthorizationReadHandler(WorkspaceContents contents, IDataScopeObjectReadHandler authorization)
    : IButtonBusinessHandler<WorkspaceAuthorizationReadInput, DataScopeObjectReadState, DataScopeAuthorizationResult<object>>
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<object>> WithinBoundaryAsync(WorkspaceAuthorizationReadInput input, string actionKey,
        Func<DataScopeObjectReadState, CancellationToken, Task<DataScopeAuthorizationResult<object>>> execute, CancellationToken token)
    {
        var target = await contents.ResolvePermissionTargetAsync(input.ActorId, input.Position, input.LinkId, token);
        return await authorization.WithinBoundaryAsync(new(target.ResourceKey, target.ObjectId, input.Operation,
            input.SubjectType, input.Query, input.Page, input.CurrentIds), actionKey, execute, token);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectReadState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<object>> ExecuteAsync(DataScopeObjectReadState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}
