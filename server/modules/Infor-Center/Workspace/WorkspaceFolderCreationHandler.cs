using System.Text.Json;
using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>当前正式位置与Folder字段；复制父配置由原明确选项选择，不改变权限要求。</summary>
public sealed record WorkspaceFolderCreationInput(string ActorId, WorkspacePosition Position,
    IReadOnlyDictionary<string, JsonElement> Values, bool CopyParentAuthorization);

/// <summary>本次服务端完整候选与原事务内唯一持久化续体。</summary>
public sealed class WorkspaceFolderCreationState
{
    public IReadOnlyDictionary<string, object?> Candidate { get; }
    internal Func<CancellationToken, Task<string>> Persist { get; }
    // 只有实际Folder创建边界形成状态，调用方不能另传候选或权限目标。
    internal WorkspaceFolderCreationState(IReadOnlyDictionary<string, object?> candidate, Func<CancellationToken, Task<string>> persist)
    { Candidate = candidate; Persist = persist; }
}

/// <summary>原图事务规范化Folder候选后，平台自动检查再进入原实体、父初始化和引用编排。</summary>
public sealed class WorkspaceFolderCreationHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspaceFolderCreationInput, WorkspaceFolderCreationState, string>
{
    /// <inheritdoc/>
    public Task<string> WithinBoundaryAsync(WorkspaceFolderCreationInput input, string actionKey,
        Func<WorkspaceFolderCreationState, CancellationToken, Task<string>> execute, CancellationToken cancellationToken) =>
        contents.WithinFolderCreationAsync(input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() } }, execute, cancellationToken);
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceFolderCreationState state) => new ButtonCreationTarget("workspace.folder", "WorkspaceFolder", state.Candidate);
    /// <inheritdoc/>
    public Task<string> ExecuteAsync(WorkspaceFolderCreationState state, CancellationToken cancellationToken) => state.Persist(cancellationToken);
}
