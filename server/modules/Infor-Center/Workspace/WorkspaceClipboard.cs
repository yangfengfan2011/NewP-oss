using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceClipboardItem(string LinkId, string ModelKey);
public sealed record WorkspaceClipboardSnapshot(string Mode, WorkspacePosition SourcePosition, IReadOnlyList<WorkspaceClipboardItem> Items);
public sealed record WorkspacePasteResult(bool Succeeded, bool NoChange);

public sealed partial class WorkspaceContents
{
    /// <summary>只读资格预检只返回已核证关系身份；前次结果不是随后写入的授权凭证。</summary>
    public async Task<WorkspaceClipboardSnapshot> ValidateClipboardAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, CancellationToken token)
    {
        RequireClipboardSelection(mode, linkIds);
        var binding = WorkspaceClipboardExecutions.Binding(mode == "copy" ? WorkspaceClipboardExecutions.Copy : WorkspaceClipboardExecutions.Cut, "prepare");
        executionAccess.Validate([binding], "workspace.folder");
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            // 一页资格检查共享请求连接；不创建事务或缓存裁决，避免每个正式读接口反复开关连接。
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            var source = await ResolveClipboardPositionAsync(binding, "sourceParent", sourcePosition, token);
            var targets = await ResolveDeclaredClipboardTargetsAsync(source.FolderId, linkIds, binding, token);
            return new(mode, sourcePosition, linkIds.Select((id, index) => new WorkspaceClipboardItem(id, targets[index].ModelKey)).ToArray());
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>图锁内重验完整位置及模式对应资格；复制或真实移动登记关系业务记录，剪切精确移除源LID，均共享原事务。</summary>
    public ValueTask<WorkspacePasteResult> PasteAsync(string actor, string mode, WorkspacePosition sourcePosition, string[] linkIds,
        WorkspacePosition targetPosition, CancellationToken token)
    {
        RequireClipboardSelection(mode, linkIds);
        var binding = WorkspaceClipboardExecutions.Binding(WorkspaceClipboardExecutions.Paste, "submit", mode);
        executionAccess.Validate([binding], "workspace.folder");
        return transactions.ExecuteAsync<WorkspacePasteResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var source = await ResolveClipboardPositionAsync(binding, "sourceParent", sourcePosition, cancellation);
            var targets = await ResolveDeclaredClipboardTargetsAsync(source.FolderId, linkIds, binding, cancellation);
            var target = await ResolveClipboardPositionAsync(binding, "targetParent", targetPosition, cancellation);
            // 同一真实父的剪切只确认资格，不走新落入规则或关系重建，包括历史根下业务对象。
            if (mode == "cut" && source.FolderId == target.FolderId) return new(true, true);
            foreach (var item in targets) RequireContainer(targetPosition, target, item.ModelKey);
            // cut重建LID但不是再次分享；在同一图锁内一次读取至多100条原边语义，连历史NULL也原样保留。
            var sourceScopes = mode == "cut" ? await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                    && EF.Property<string>(row, "PEID") == source.FolderId)
                .Select(row => new { Id = EF.Property<string>(row, "LID"), Neutral = EF.Property<bool?>(row, "IsScopeNeutral") })
                .ToDictionaryAsync(row => row.Id, row => row.Neutral, cancellation) : null;
            // 名称按真实数据库collation比较，覆盖整批内部冲突，不能用客户端或CLR名称比较代替。
            var folders = targets.Where(item => item.ModelKey == WorkspacePositions.FolderModel).Select(item => item.Id).ToArray();
            if (folders.Length > 1 && await Folders.Where(row => folders.Contains(EF.Property<string>(row, "EID")))
                .GroupBy(row => EF.Property<string>(row, "ENAME")).AnyAsync(group => group.Count() > 1, cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            // 至多一页100项，保持选择次序；同事务未保存关系由正式NextOrdinalAsync参与末尾计算。
            for (var index = 0; index < targets.Length; index++)
            {
                var linkId = await AddTargetReferenceAsync(target.FolderId, targets[index], cancellation, sourceScopes is null ? false : sourceScopes[linkIds[index]]);
                if (mode == "copy") await referenceAudit.CopiedAsync(linkIds[index], source.FolderId, linkId, target.FolderId, targets[index], cancellation);
                else await referenceAudit.MovedAsync(linkIds[index], source.FolderId, linkId, target.FolderId, targets[index], cancellation);
            }
            if (mode == "cut") await Links.Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == source.FolderId).ExecuteDeleteAsync(cancellation);
            return new(true, false);
        }, token);
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
                binding.Execution == WorkspaceClipboardExecutions.Cut || binding.Branch == "cut" ? "Workspace.ObjectWriteDenied" : "Workspace.ObjectCopyDenied");
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
            if (target.ModelKey == "Document")
            {
                await foreach (var result in executionAccess.EvaluateObjectsAsync(WorkspaceObjectCopyExecutions.Binding(stage, "Document", batch),
                    batch ? "visibility" : "confirmationVisibility", [new("document.document", target.Id, target)], token))
                    if (result.Decision != DataExecutionDecision.Allowed) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            }
            else if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
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
    private async Task<string> AddTargetReferenceAsync(string parentId, LinkObjectReference target, CancellationToken cancellation, bool? isScopeNeutral = false)
    {
        var definition = catalog.Get("workspace.content");
        if (string.IsNullOrWhiteSpace(target.Id) || !definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == target.ModelKey))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint", "target");
        if (!await reader.IsEndpointReadableAsync("workspace.content", target, cancellation)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
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
