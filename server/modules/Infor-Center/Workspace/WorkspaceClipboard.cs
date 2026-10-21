using System.Collections.Frozen;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceClipboardItem(string LinkId, string ModelKey);
public sealed record WorkspaceClipboardSnapshot(string Mode, WorkspacePosition SourcePosition, IReadOnlyList<WorkspaceClipboardItem> Items);
public sealed record WorkspacePasteResult(bool Succeeded, bool NoChange);

public sealed partial class WorkspaceContents
{
    // 选择预检保留原请求连接生命周期；输入快照只属于本次调用，不成为后续提交允许凭证。
    internal async Task<WorkspaceClipboardSnapshot> WithClipboardSelectionBoundaryAsync(WorkspaceClipboardSelectionInput input, string actionKey,
        Func<WorkspaceClipboardSelectionState, CancellationToken, Task<WorkspaceClipboardSnapshot>> execute, CancellationToken token)
    {
        RequireClipboardSelection(input.Mode, input.LinkIds);
        if (actionKey != input.Mode && actionKey != input.Mode + "Selected") throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        var position = FreezeClipboardPosition(input.SourcePosition);
        var selection = Array.AsReadOnly(input.LinkIds.ToArray());
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            var source = await positions.ResolveAsync(input.ActorId, position, token);
            return await execute(new(input.Mode, position, source.FolderId, ReferenceParentFacts(source), selection), token);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    // 父要求通过平台后才按原顺序检查关联端点；copy选择保留引用复制或真副本用途的OR。
    internal async Task<WorkspaceClipboardSnapshot> ExecuteClipboardSelectionAsync(WorkspaceClipboardSelectionState state, CancellationToken token)
    {
        var targets = await ReadClipboardButtonTargetsAsync(state.ParentId, state.LinkIds.ToArray(), state.Mode, true, token);
        return new(state.Mode, state.SourcePosition, state.LinkIds.Select((id, index) => new WorkspaceClipboardItem(id, targets[index].ModelKey)).ToArray());
    }

    // 原图事务内先重解来源及关联端点，再把真实目标父交给平台；事务不会因准备或判权失败部分提交。
    internal Task<WorkspacePasteResult> WithPasteReferencesBoundaryAsync(WorkspacePasteReferencesInput input, string actionKey,
        Func<WorkspacePasteReferencesState, CancellationToken, Task<WorkspacePasteResult>> execute, CancellationToken token)
    {
        RequireClipboardSelection(input.Mode, input.LinkIds);
        if (actionKey != "paste") throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        var sourcePosition = FreezeClipboardPosition(input.SourcePosition);
        var targetPosition = FreezeClipboardPosition(input.TargetPosition);
        var selection = Array.AsReadOnly(input.LinkIds.ToArray());
        return transactions.ExecuteAsync<WorkspacePasteResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var source = await positions.ResolveAsync(input.ActorId, sourcePosition, cancellation);
            if (input.Mode == "cut" && !await objectDecisions.IsAllowedAsync("workspace.folder", source.FolderId, "edit", cancellation))
                throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
            var targets = await ReadClipboardButtonTargetsAsync(source.FolderId, selection.ToArray(), input.Mode, false, cancellation);
            var target = await positions.ResolveAsync(input.ActorId, targetPosition, cancellation);
            return await execute(new(input.Mode, sourcePosition, source, targetPosition, target, ReferenceParentFacts(target),
                selection, Array.AsReadOnly(targets)), cancellation);
        }, token).AsTask();
    }

    // 两父、端点和按钮要求全部通过后沿原关系写入与审计顺序执行；同位cut只回执不重建。
    internal async Task<WorkspacePasteResult> ExecutePasteReferencesAsync(WorkspacePasteReferencesState state, CancellationToken token)
    {
        var source = state.Source;
        var target = state.Target;
        var targets = state.Targets;
        var linkIds = state.LinkIds.ToArray();
        var mode = state.Mode;
        if (mode == "cut" && source.FolderId == target.FolderId) return new(true, true);
        foreach (var item in targets) RequireContainer(state.TargetPosition, target, item.ModelKey);
        var sourceScopes = mode == "cut" ? await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == source.FolderId)
            .Select(row => new { Id = EF.Property<string>(row, "LID"), Neutral = EF.Property<bool?>(row, "IsScopeNeutral") })
            .ToDictionaryAsync(row => row.Id, row => row.Neutral, token) : null;
        var folders = targets.Where(item => item.ModelKey == WorkspacePositions.FolderModel).Select(item => item.Id).ToArray();
        if (folders.Length > 1 && await Folders.Where(row => folders.Contains(EF.Property<string>(row, "EID")))
            .GroupBy(row => EF.Property<string>(row, "ENAME")).AnyAsync(group => group.Count() > 1, token))
            throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
        for (var index = 0; index < targets.Count; index++)
        {
            var linkId = await AddTargetReferenceAsync(target.FolderId, targets[index], ReferenceReadSource.Clipboard, token,
                sourceScopes is null ? false : sourceScopes[linkIds[index]]);
            if (mode == "copy") await referenceAudit.CopiedAsync(linkIds[index], source.FolderId, linkId, target.FolderId, targets[index], token);
            else await referenceAudit.MovedAsync(linkIds[index], source.FolderId, linkId, target.FolderId, targets[index], token);
        }
        if (mode == "cut") await Links.Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
            && EF.Property<string>(row, "PEID") == source.FolderId).ExecuteDeleteAsync(token);
        return new(true, false);
    }

    // 只冻结正式位置，不接受客户端额外对象身份或允许结果。
    private static WorkspacePosition FreezeClipboardPosition(WorkspacePosition position) => position?.LinkIds is not null
        ? position with { LinkIds = Array.AsReadOnly(position.LinkIds.ToArray()) }
        : throw new LinkQueryException(400, "Workspace.InvalidPosition");

    // 引用不改父字段，原完整位置已读取真实父身份；规则事实仍由访问服务重验，不新增父实体物化或允许缓存。
    private static IReadOnlyDictionary<string, object?> ReferenceParentFacts(WorkspacePositionPath path) =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["EID"] = path.FolderId }.ToFrozenDictionary(StringComparer.Ordinal);

    // 继续原Link完整read，再由通用裁决引擎检查实际用途；OR只属于copy选择业务，不成为新权限配置。
    private async Task<LinkObjectReference[]> ReadClipboardButtonTargetsAsync(string parentId, string[] linkIds, string mode,
        bool selection, CancellationToken token)
    {
        var targets = await ReadClipboardTargetsAsync(parentId, linkIds, token);
        // 先核整批存储类型，保留原损坏来源首错；对象读取和动作仍逐项执行。
        var endpoints = targets.Select(target => (Target: target, ResourceKey: Resource(target.ModelKey))).ToArray();
        foreach (var (target, resourceKey) in endpoints)
        {
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            string[] actions = mode == "cut" ? ["edit"] : target.ModelKey switch
            {
                WorkspacePositions.FolderModel => ["workspaceCopy"],
                "Part" => selection ? ["partCopy", "partObjectCopy"] : ["partCopy"],
                "Document" => selection ? ["documentCopy", "documentObjectCopy"] : ["documentCopy"],
                _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
            };
            var allowed = false;
            foreach (var action in actions)
                if (await objectDecisions.IsAllowedAsync(resourceKey, target.Id, action, token)) { allowed = true; break; }
            if (!allowed) throw new LinkQueryException(403, mode == "cut" ? "Workspace.ObjectWriteDenied" : "Workspace.ObjectCopyDenied");
            await RequireNonRootClipboardTargetAsync(target, token);
        }
        return targets;
    }

    // 唯一关系解析保持当前父和请求次序；各执行在自己的正式边界上消费同一真实目标。
    private async Task<LinkObjectReference[]> ReadClipboardTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var rows = await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == parentId)
            .Select(row => new { LinkId = EF.Property<string>(row, "LID"), Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != linkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var byId = rows.ToDictionary(row => row.LinkId, StringComparer.Ordinal);
        var targets = linkIds.Select(id => byId.TryGetValue(id, out var row) ? new LinkObjectReference(row.Model, row.Id)
            : throw new LinkQueryException(404, "Workspace.ReferenceUnavailable")).ToArray();
        return targets;
    }

    // 统一要求按输入顺序惰性裁决；每项根约束必须先于下一项，不能把错误排序改成全read后全动作。
    private async Task<LinkObjectReference[]> ResolveDeclaredClipboardTargetsAsync(string parentId, string[] linkIds,
        DataExecutionBindingReference binding, CancellationToken token)
    {
        var targets = await ReadClipboardTargetsAsync(parentId, linkIds, token);
        var input = targets.Select(target => new DataExecutionTargetInput(Resource(target.ModelKey), target.Id, target)).ToArray();
        var index = 0;
        await foreach (var result in executionAccess.EvaluateObjectsAsync(binding, "sourceTargets", input, token))
        {
            if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403,
                binding.Branch == "cut" ? "Workspace.ObjectWriteDenied" : "Workspace.ObjectCopyDenied");
            var target = targets[index++];
            if (result.Target?.ObjectId != target.Id || result.Target.ResourceKey != Resource(target.ModelKey))
                throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
            await RequireNonRootClipboardTargetAsync(target, token);
        }
        if (index != targets.Length) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        return targets;
    }

    // 整批原Link完整read在同一位置消费正式visibility，不增加逐项额外read。
    private async Task<LinkObjectReference[]> ResolveDocumentCopyTargetsAsync(string parentId, string[] linkIds,
        string stage, CancellationToken token, bool batch = true)
    {
        var targets = await ReadClipboardTargetsAsync(parentId, linkIds, token);
        foreach (var target in targets)
        {
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            await RequireNonRootClipboardTargetAsync(target, token);
        }
        return targets;
    }

    // 根身份取当前服务端空间事实；此业务约束保持在每个目标裁决之后。
    private async Task RequireNonRootClipboardTargetAsync(LinkObjectReference target, CancellationToken token)
    {
        if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("WorkspaceSpace")
            .AnyAsync(row => EF.Property<string>(row, "RootFolderId") == target.Id, token))
            throw new LinkQueryException(409, "Workspace.RootReferenceForbidden");
    }

    // 根接收规则只限制新落入，不改变已有关系读取、编辑、删除与移出。
    private static void RequireContainer(WorkspacePosition position, WorkspacePositionPath path, string model)
    {
        if (path.FolderId == position.RootFolderId && model != WorkspacePositions.FolderModel)
            throw new LinkQueryException(400, "Workspace.RootOnlyFolders");
    }

    // 模式不能被忽略或静默降级，选择边界沿现有一页规则。
    private static void RequireClipboardSelection(string mode, string[] linkIds)
    {
        if (mode is not ("copy" or "cut")) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        ValidateSelection(linkIds);
    }

    // 单条add与批量paste共享完整图校验；显式分享为普通边，cut调用方传入原边语义，均已持图锁。
    private async Task<string> AddTargetReferenceAsync(string parentId, LinkObjectReference target, ReferenceReadSource source, CancellationToken cancellation, bool? isScopeNeutral = false)
    {
        var definition = catalog.Get("workspace.content");
        if (string.IsNullOrWhiteSpace(target.Id) || !definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == target.ModelKey))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint", "target");
        if (source == ReferenceReadSource.Api) await RequireReferenceTargetAsync(target, cancellation);
        else if (source == ReferenceReadSource.Clipboard)
        {
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, cancellation)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        }
        else throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(space => EF.Property<string>(space, "RootFolderId") == target.Id, cancellation))
            throw new LinkQueryException(409, "Workspace.RootReferenceForbidden");
        var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
        if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == parentId && EF.Property<string>(link, "ChildModelKey") == target.ModelKey
            && EF.Property<string>(link, "CEID") == target.Id, cancellation)) throw new LinkQueryException(409, "Workspace.DuplicateReference");
        if (target.ModelKey == WorkspacePositions.FolderModel)
        {
            var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);
            var name = await folders.Where(folder => EF.Property<string>(folder, "EID") == target.Id).Select(folder => EF.Property<string>(folder, "ENAME")).SingleAsync(cancellation);
            if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == parentId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                && folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name), cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            if (await WouldCycleAsync(parentId, target.Id, cancellation)) throw new LinkQueryException(409, "Workspace.CycleForbidden");
        }
        return await RegisterCreatedReferenceAsync(parentId, target.ModelKey, target.Id, cancellation, isScopeNeutral);
    }
}
