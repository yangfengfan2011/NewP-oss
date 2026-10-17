using ICP.Platform.Crud.LinkQueries;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    /// <summary>在唯一图锁事务内确定不同Folder及全部内部边，完整核验后按旧新映射一次写入。</summary>
    public ValueTask<WorkspacePasteResult> CopyFolderStructureAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, string name, CancellationToken token)
    {
        if (mode != "copy" || linkIds.Length != 1 || string.IsNullOrWhiteSpace(linkIds[0])) throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        return transactions.ExecuteAsync<WorkspacePasteResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var sourceParent = await positions.ResolveAsync(actor, sourcePosition, cancellation);
            var source = (await ResolveClipboardTargetsAsync(sourceParent.FolderId, linkIds, "copy", cancellation))[0];
            if (source.ModelKey != WorkspacePositions.FolderModel) throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
            var target = await WritablePositionAsync(actor, targetPosition, cancellation);
            if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == WorkspacePositions.FolderModel))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            var nodes = new Dictionary<string, WorkspaceStructureFolder>(StringComparer.OrdinalIgnoreCase);
            var edges = new List<WorkspaceStructureEdge>(); var pending = new Queue<string>(); var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.Id };
            pending.Enqueue(source.Id);
            // 只遍历Folder边，每个EID排队一次；不枚举出现路径，不读取排除的Part/Document端点或文件。
            while (pending.TryDequeue(out var id))
            {
                cancellation.ThrowIfCancellationRequested();
                var folder = await Folders.AsNoTracking().Where(row => EF.Property<string>(row, "EID") == id)
                    .Select(row => new WorkspaceStructureFolder(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), EF.Property<string?>(row, "Description"))).SingleOrDefaultAsync(cancellation);
                if (folder is null) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                if (!await reader.IsEndpointReadableAsync("workspace.content", new(WorkspacePositions.FolderModel, id), cancellation)
                    || !await decisions.IsAllowedAsync("workspace.folder", id, "workspaceCopy", cancellation)) throw new LinkQueryException(403, "Workspace.FolderCopyUnavailable");
                if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => EF.Property<string>(row, "RootFolderId") == id, cancellation))
                    throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                nodes.Add(id, folder);
                // 名称相等由真实数据库collation决定，不能用应用字符串比较替代。
                if (await Links.Where(row => EF.Property<string>(row, "PEID") == id && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel)
                    .Join(Folders, link => EF.Property<string>(link, "CEID"), child => EF.Property<string>(child, "EID"), (link, child) => EF.Property<string>(child, "ENAME"))
                    .GroupBy(value => value).AnyAsync(group => group.Count() > 1, cancellation)) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                string? last = null;
                while (true)
                {
                    var query = Links.AsNoTracking().Where(row => EF.Property<string>(row, "PEID") == id && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel);
                    if (last is not null) query = query.Where(row => string.Compare(EF.Property<string>(row, "LID"), last) > 0);
                    var batch = await query.OrderBy(row => EF.Property<string>(row, "LID")).Take(256).Select(row => new WorkspaceStructureEdge(
                        EF.Property<string>(row, "LID"), id, EF.Property<string>(row, "CEID"), EF.Property<long?>(row, "TreeOrdinal"))).ToArrayAsync(cancellation);
                    foreach (var edge in batch) { edges.Add(edge); if (discovered.Add(edge.Child)) pending.Enqueue(edge.Child); }
                    if (batch.Length < 256) break;
                    last = batch[^1].LinkId;
                }
            }
            var plan = WorkspaceFolderStructurePlan.Create(source.Id, nodes.Values.ToArray(), edges);
            if (await Links.AnyAsync(link => EF.Property<string>(link, "PEID") == target.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                && Folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name), cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            var candidates = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in plan.Folders) candidates.Add(folder.Id, await PrepareFolderCandidateAsync(StringComparer.OrdinalIgnoreCase.Equals(folder.Id, source.Id) ? name : folder.Name, folder.Description, targetPosition.SpaceId, cancellation));
            // 全部候选及最终图先成立，之后才产生任何持久写入；新图只指向全新EID，目标为源后代也不会成环。
            foreach (var candidate in candidates.Values)
            {
                Folders.Add(candidate);
                await ownershipAudit.RegisterAsync("workspace.folder", (string)candidate["EID"]!, candidate, false, cancellation);
            }
            await transactions.FlushAsync(cancellation);
            var createdRoot = (string)candidates[source.Id]["EID"]!;
            var rootLink = await RegisterCreatedReferenceAsync(target.FolderId, WorkspacePositions.FolderModel, createdRoot, cancellation);
            await referenceAudit.CopiedAsync(linkIds[0], sourceParent.FolderId, rootLink, target.FolderId, new(WorkspacePositions.FolderModel, createdRoot), cancellation);
            foreach (var group in plan.Edges.GroupBy(edge => edge.Parent, StringComparer.OrdinalIgnoreCase))
            {
                long ordinal = 0;
                foreach (var edge in group.OrderBy(edge => edge.Ordinal))
                {
                    var parent = (string)candidates[edge.Parent]["EID"]!; var child = (string)candidates[edge.Child]["EID"]!;
                    var lid = RegisterCopiedReference(parent, new(edge.LinkId, WorkspacePositions.FolderModel, child, edge.Ordinal), ++ordinal, isScopeNeutral: false);
                    await referenceAudit.CopiedAsync(edge.LinkId, edge.Parent, lid, parent, new(WorkspacePositions.FolderModel, child), cancellation);
                }
            }
            foreach (var folder in plan.Folders) await referenceAudit.StructureCopiedAsync(folder.Id, (string)candidates[folder.Id]["EID"]!, cancellation);
            return new(true, false);
        }, token);
    }
}

public sealed record WorkspaceStructureFolder(string Id, string Name, string? Description);
public sealed record WorkspaceStructureEdge(string LinkId, string Parent, string Child, long? Ordinal);

/// <summary>模块内的纯图计划；节点和边各处理一次，不展开共享图的出现路径。</summary>
public sealed record WorkspaceFolderStructurePlan(IReadOnlyList<WorkspaceStructureFolder> Folders, IReadOnlyList<WorkspaceStructureEdge> Edges)
{
    /// <summary>拒绝不完整、重复、次序损坏或有环的实际图，返回按依赖次序排列的不同Folder。</summary>
    public static WorkspaceFolderStructurePlan Create(string root, IReadOnlyList<WorkspaceStructureFolder> folders, IReadOnlyList<WorkspaceStructureEdge> edges)
    {
        var nodes = new Dictionary<string, WorkspaceStructureFolder>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders) if (!nodes.TryAdd(folder.Id, folder)) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
        if (!nodes.ContainsKey(root) || edges.Any(edge => !nodes.ContainsKey(edge.Parent) || !nodes.ContainsKey(edge.Child) || edge.Ordinal is null or <= 0)
            || edges.Select(edge => edge.LinkId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != edges.Count
            || edges.GroupBy(edge => (edge.Parent.ToUpperInvariant(), edge.Child.ToUpperInvariant())).Any(group => group.Count() > 1)
            || edges.GroupBy(edge => (edge.Parent.ToUpperInvariant(), edge.Ordinal)).Any(group => group.Count() > 1)) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
        var outgoing = edges.ToLookup(edge => edge.Parent, StringComparer.OrdinalIgnoreCase);
        var degrees = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges) degrees[edge.Child]++;
        var pending = new Queue<string>(); pending.Enqueue(root); var ordered = new List<WorkspaceStructureFolder>();
        if (degrees[root] != 0) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
        while (pending.TryDequeue(out var id)) { ordered.Add(nodes[id]); foreach (var edge in outgoing[id]) if (--degrees[edge.Child] == 0) pending.Enqueue(edge.Child); }
        if (ordered.Count != nodes.Count) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
        return new(ordered, edges);
    }
}
