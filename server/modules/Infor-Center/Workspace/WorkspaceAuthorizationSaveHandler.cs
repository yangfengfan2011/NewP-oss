using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>真实出现位置与LID只由Workspace解释，配置不能覆盖规范对象。</summary>
public sealed record WorkspaceAuthorizationSaveInput(string ActorId, WorkspacePosition Position, string LinkId,
    DataScopeObjectConfiguration Configuration);

/// <summary>先持有原图锁并解析同一来源，再进入共享ACL保护和保存续体。</summary>
public sealed class WorkspaceAuthorizationSaveHandler(WorkspaceContents contents, IDataScopeObjectSaveHandler authorization)
    : IButtonBusinessHandler<WorkspaceAuthorizationSaveInput, DataScopeObjectSaveState, DataScopeAuthorizationResult>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult> WithinBoundaryAsync(WorkspaceAuthorizationSaveInput input, string actionKey,
        Func<DataScopeObjectSaveState, CancellationToken, Task<DataScopeAuthorizationResult>> execute, CancellationToken token) =>
        contents.WithinPermissionWriteAsync(input.ActorId, input.Position, input.LinkId, (target, cancellation) =>
            authorization.WithinBoundaryAsync(new(target.ResourceKey, target.ObjectId, input.Configuration), actionKey, execute, cancellation), token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectSaveState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult> ExecuteAsync(DataScopeObjectSaveState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}
