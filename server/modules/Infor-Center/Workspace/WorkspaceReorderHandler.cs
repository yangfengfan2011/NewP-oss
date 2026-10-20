using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式排序输入只持有服务器主体、完整父位置、所选关系和方向。</summary>
public sealed record WorkspaceReorderInput(string ActorId, WorkspacePosition ParentPosition, string LinkId, string Direction);
/// <summary>原图锁内规范化的父对象与所选关系；平台检查和排序共用同一状态。</summary>
public sealed record WorkspaceReorderState(string ActorId, string ParentId, string LinkId, bool Up,
    IReadOnlyDictionary<string, object?> Before, WorkspacePosition ParentPosition);

/// <summary>排序只提供真实业务边界和目标，不手工调用按钮入口判权。</summary>
public sealed class WorkspaceReorderHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspaceReorderInput, WorkspaceReorderState, WorkspaceTreeOrderResult>
{
    /// <inheritdoc/>
    public Task<WorkspaceTreeOrderResult> WithinBoundaryAsync(WorkspaceReorderInput input, string actionKey,
        Func<WorkspaceReorderState, CancellationToken, Task<WorkspaceTreeOrderResult>> execute, CancellationToken cancellationToken) =>
        contents.WithReorderBoundaryAsync(input, actionKey, execute, cancellationToken).AsTask();
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceReorderState state) =>
        new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", state.ParentId, state.Before) { ReadContext = state.ParentPosition };
    /// <inheritdoc/>
    public Task<WorkspaceTreeOrderResult> ExecuteAsync(WorkspaceReorderState state, CancellationToken cancellationToken) =>
        contents.ExecuteReorderAsync(state, cancellationToken);
}
