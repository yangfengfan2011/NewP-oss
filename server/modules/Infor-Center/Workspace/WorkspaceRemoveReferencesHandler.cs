using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>引用移除只接受当前出现位置与所选LID，不接受对象delete目标。</summary>
public sealed record WorkspaceRemoveReferencesInput(string ActorId, WorkspacePosition Position, string[] LinkIds);
/// <summary>原图锁内规范化的真实父及独立不可变输入，平台检查与关系删除共用此状态。</summary>
public sealed record WorkspaceRemoveReferencesState(string ParentId, IReadOnlyDictionary<string, object?> Before,
    WorkspacePosition Position, IReadOnlyList<string> LinkIds);

/// <summary>平台自动检查实际父edit，业务保留逐项端点read、根约束与精确关系删除。</summary>
public sealed class WorkspaceRemoveReferencesHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspaceRemoveReferencesInput, WorkspaceRemoveReferencesState, bool>
{
    /// <inheritdoc/>
    public Task<bool> WithinBoundaryAsync(WorkspaceRemoveReferencesInput input, string actionKey,
        Func<WorkspaceRemoveReferencesState, CancellationToken, Task<bool>> execute, CancellationToken token) =>
        contents.WithRemoveReferencesBoundaryAsync(input, actionKey, execute, token).AsTask();
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceRemoveReferencesState state) =>
        new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", state.ParentId, state.Before) { ReadContext = state.Position };
    /// <inheritdoc/>
    public Task<bool> ExecuteAsync(WorkspaceRemoveReferencesState state, CancellationToken token) =>
        contents.ExecuteRemoveReferencesAsync(state, token);
}

public sealed partial class WorkspaceContents
{
    // 原事务与图锁先保护完整父位置；不把所选对象或LID当作被编辑的父Folder。
    internal ValueTask<bool> WithRemoveReferencesBoundaryAsync(WorkspaceRemoveReferencesInput input, string actionKey,
        Func<WorkspaceRemoveReferencesState, CancellationToken, Task<bool>> execute, CancellationToken token)
    {
        if (input.Position?.LinkIds is null || input.LinkIds is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
        ValidateSelection(input.LinkIds);
        if (actionKey is not ("remove" or "removeSelected") || actionKey == "remove" && input.LinkIds.Length != 1)
            throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
        var position = input.Position with { LinkIds = Array.AsReadOnly(input.Position.LinkIds.ToArray()) };
        var selection = Array.AsReadOnly(input.LinkIds.ToArray());
        return transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await positions.ResolveAsync(input.ActorId, position, cancellation);
            return await execute(new(path.FolderId, ReferenceParentFacts(path), position, selection), cancellation);
        }, token);
    }

    // 全部读取与业务守卫通过后才删除确切LID；不调用对象writer或对象delete动作。
    internal async Task<bool> ExecuteRemoveReferencesAsync(WorkspaceRemoveReferencesState state, CancellationToken token)
    {
        var selection = state.LinkIds.ToArray();
        var targets = await ReadClipboardTargetsAsync(state.ParentId, selection, token);
        foreach (var target in targets)
        {
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token))
                throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            await RequireNonRootClipboardTargetAsync(target, token);
        }
        await Links.Where(row => selection.Contains(EF.Property<string>(row, "LID"))
            && EF.Property<string>(row, "PEID") == state.ParentId).ExecuteDeleteAsync(token);
        for (var index = 0; index < targets.Length; index++)
            await referenceAudit.RemovedAsync(selection[index], state.ParentId, targets[index], token);
        return true;
    }
}
