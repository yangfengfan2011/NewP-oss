using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceClipboardItem(string LinkId);
public sealed record WorkspaceClipboardSnapshot(string Mode, WorkspacePosition SourcePosition, IReadOnlyList<WorkspaceClipboardItem> Items);

public sealed partial class WorkspaceContents
{
    /// <summary>只读资格预检只返回已核证关系身份；前次结果不是随后写入的授权凭证。</summary>
    public async Task<WorkspaceClipboardSnapshot> ValidateClipboardAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, CancellationToken token)
    {
        RequireCopySelection(mode, linkIds);
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            // 一页资格检查共享请求连接；不创建事务或缓存裁决，避免每个正式读接口反复开关连接。
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            var source = await positions.ResolveAsync(actor, sourcePosition, token);
            await ResolveCopyTargetsAsync(source.FolderId, linkIds, token);
            return new(mode, sourcePosition, linkIds.Select(id => new WorkspaceClipboardItem(id)).ToArray());
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>源完整路径、关系和所属copy资格与目标父edit在同一图锁事务重验，整批只新增引用。</summary>
    public ValueTask<bool> PasteAsync(string actor, string mode, WorkspacePosition sourcePosition, string[] linkIds,
        WorkspacePosition targetPosition, CancellationToken token)
    {
        RequireCopySelection(mode, linkIds);
        return transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var source = await positions.ResolveAsync(actor, sourcePosition, cancellation);
            var targets = await ResolveCopyTargetsAsync(source.FolderId, linkIds, cancellation);
            var target = await WritablePositionAsync(actor, targetPosition, cancellation);
            foreach (var item in targets) RequireContainer(targetPosition, target, item.ModelKey);
            // 名称按真实数据库collation比较，覆盖整批内部冲突，不能用客户端或CLR名称比较代替。
            var folders = targets.Where(item => item.ModelKey == WorkspacePositions.FolderModel).Select(item => item.Id).ToArray();
            if (folders.Length > 1 && await Folders.Where(row => folders.Contains(EF.Property<string>(row, "EID")))
                .GroupBy(row => EF.Property<string>(row, "ENAME")).AnyAsync(group => group.Count() > 1, cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            // 至多一页100项，保持选择次序；同事务未保存关系由正式NextOrdinalAsync参与末尾计算。
            foreach (var item in targets) await AddTargetReferenceAsync(target.FolderId, item, cancellation);
            return true;
        }, token);
    }

    // 真实关系按请求顺序返回；不按类型排序、不接受客户端EID，也不把源父edit变成copy条件。
    private async Task<LinkObjectReference[]> ResolveCopyTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var rows = await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == parentId)
            .Select(row => new { LinkId = EF.Property<string>(row, "LID"), Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != linkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var byId = rows.ToDictionary(row => row.LinkId, StringComparer.Ordinal);
        var targets = linkIds.Select(id => byId.TryGetValue(id, out var row) ? new LinkObjectReference(row.Model, row.Id)
            : throw new LinkQueryException(404, "Workspace.ReferenceUnavailable")).ToArray();
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (!await decisions.IsAllowedAsync(resource, target.Id, CopyAction(target.ModelKey), token)) throw new LinkQueryException(403, "Workspace.ObjectCopyDenied");
            if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("WorkspaceSpace")
                .AnyAsync(row => EF.Property<string>(row, "RootFolderId") == target.Id, token)) throw new LinkQueryException(409, "Workspace.RootReferenceForbidden");
        }
        return targets;
    }

    // 数据动作身份由各真实对象owner独立声明，遵循正式目录全局唯一性，不借用edit/delete。
    private static string CopyAction(string model) => model switch
    {
        WorkspacePositions.FolderModel => "workspaceCopy", "Part" => "partCopy", "Document" => "documentCopy",
        _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
    };

    // 根接收规则只限制新落入，不改变已有关系读取、编辑、删除与移出。
    private static void RequireContainer(WorkspacePosition position, WorkspacePositionPath path, string model)
    {
        if (path.FolderId == position.RootFolderId && model != WorkspacePositions.FolderModel)
            throw new LinkQueryException(400, "Workspace.RootOnlyFolders");
    }

    // 未实施cut只接受copy；模式不能被忽略或静默降级，选择边界沿现有一页规则。
    private static void RequireCopySelection(string mode, string[] linkIds)
    {
        if (mode != "copy") throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        ValidateSelection(linkIds);
    }

    // 单条add与批量paste共享真实端点及完整存储图不变量；调用方已持有图锁。
    private async Task AddTargetReferenceAsync(string parentId, LinkObjectReference target, CancellationToken cancellation)
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
        await RegisterCreatedReferenceAsync(parentId, target.ModelKey, target.Id, cancellation);
    }
}
