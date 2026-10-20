using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式选择只携带来源位置与有序关系，不能由客户端指定真实对象身份。</summary>
public sealed record WorkspaceClipboardSelectionInput(string ActorId, string Mode, WorkspacePosition SourcePosition, string[] LinkIds);
/// <summary>本次来源父及只读输入快照，选择结果不保存后续粘贴允许结果。</summary>
public sealed record WorkspaceClipboardSelectionState(string Mode, WorkspacePosition SourcePosition, string ParentId,
    IReadOnlyDictionary<string, object?> Before, IReadOnlyList<string> LinkIds);
/// <summary>粘贴使用完整来源和目标位置，copy/cut仍属于原业务语义。</summary>
public sealed record WorkspacePasteReferencesInput(string ActorId, string Mode, WorkspacePosition SourcePosition, string[] LinkIds, WorkspacePosition TargetPosition);
/// <summary>同一图事务内解析的父路径与真实端点供平台检查和引用变更共同消费。</summary>
public sealed record WorkspacePasteReferencesState(string Mode, WorkspacePosition SourcePosition, WorkspacePositionPath Source,
    WorkspacePosition TargetPosition, WorkspacePositionPath Target, IReadOnlyDictionary<string, object?> Before,
    IReadOnlyList<string> LinkIds, IReadOnlyList<LinkObjectReference> Targets);

/// <summary>复制和剪切选择以实际父位置为按钮目标，关联端点保持原逐项用途检查。</summary>
public sealed class WorkspaceClipboardSelectionHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspaceClipboardSelectionInput, WorkspaceClipboardSelectionState, WorkspaceClipboardSnapshot>
{
    /// <inheritdoc/>
    public Task<WorkspaceClipboardSnapshot> WithinBoundaryAsync(WorkspaceClipboardSelectionInput input, string actionKey,
        Func<WorkspaceClipboardSelectionState, CancellationToken, Task<WorkspaceClipboardSnapshot>> execute, CancellationToken token) =>
        contents.WithClipboardSelectionBoundaryAsync(input, actionKey, execute, token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceClipboardSelectionState state) =>
        new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", state.ParentId, state.Before) { ReadContext = state.SourcePosition };

    /// <inheritdoc/>
    public Task<WorkspaceClipboardSnapshot> ExecuteAsync(WorkspaceClipboardSelectionState state, CancellationToken token) =>
        contents.ExecuteClipboardSelectionAsync(state, token);
}

/// <summary>粘贴在原图锁和事务内完成来源检查，平台检查同一实际目标父后才修改引用。</summary>
public sealed class WorkspacePasteReferencesHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspacePasteReferencesInput, WorkspacePasteReferencesState, WorkspacePasteResult>
{
    /// <inheritdoc/>
    public Task<WorkspacePasteResult> WithinBoundaryAsync(WorkspacePasteReferencesInput input, string actionKey,
        Func<WorkspacePasteReferencesState, CancellationToken, Task<WorkspacePasteResult>> execute, CancellationToken token) =>
        contents.WithPasteReferencesBoundaryAsync(input, actionKey, execute, token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspacePasteReferencesState state) =>
        new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", state.Target.FolderId, state.Before) { ReadContext = state.TargetPosition };

    /// <inheritdoc/>
    public Task<WorkspacePasteResult> ExecuteAsync(WorkspacePasteReferencesState state, CancellationToken token) =>
        contents.ExecutePasteReferencesAsync(state, token);
}
