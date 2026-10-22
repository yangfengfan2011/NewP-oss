using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Authorization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    /// <summary>在唯一图锁事务内确定不同Folder及全部内部边，完整核验后按旧新映射一次写入。</summary>
    public ValueTask<WorkspacePasteResult> CopyFolderStructureAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, string name, CancellationToken token) =>
        CopyFolderStructureAsync(actor, mode, sourcePosition, linkIds, targetPosition, name, false, token);

    /// <summary>显式初始化按拓扑依赖采用目标父和全部新父的等价配置；共享Folder始终只有一个新对象。</summary>
    public ValueTask<WorkspacePasteResult> CopyFolderStructureAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, string name, bool copyParentAuthorization, CancellationToken token)
    {
        if (mode != "copy" || linkIds.Length != 1 || string.IsNullOrWhiteSpace(linkIds[0])) throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        sourcePosition = FreezeClipboardPosition(sourcePosition);
        targetPosition = FreezeClipboardPosition(targetPosition);
        linkIds = linkIds.ToArray();
        return transactions.ExecuteAsync<WorkspacePasteResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var sourceParent = await positions.ResolveAsync(actor, sourcePosition, cancellation);
            var source = await ReadFolderCopyButtonSourceAsync(sourceParent.FolderId, linkIds, cancellation);
            var transaction = db.Database.CurrentTransaction;
            return await ExecuteFolderCopyButtonAsync(folderCopyHandler, source,
                () => transactions.IsActive && transaction is not null && ReferenceEquals(transaction, db.Database.CurrentTransaction), async checkedToken =>
            {
                await RequireNonRootClipboardTargetAsync(source, checkedToken);
                var target = await ResolveWritableButtonPositionAsync(actor, targetPosition, checkedToken);
                if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == WorkspacePositions.FolderModel))
                    throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
                var nodes = new Dictionary<string, WorkspaceStructureFolder>(StringComparer.OrdinalIgnoreCase);
                var edges = new List<WorkspaceStructureEdge>(); var pending = new Queue<string>(); var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.Id };
                pending.Enqueue(source.Id);
                // 只遍历Folder边，每个EID排队一次；不枚举出现路径，不读取排除的Part/Document端点或文件。
                while (pending.TryDequeue(out var id))
                {
                    checkedToken.ThrowIfCancellationRequested();
                    var folder = await Folders.AsNoTracking().Where(row => EF.Property<string>(row, "EID") == id)
                        .Select(row => new WorkspaceStructureFolder(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), EF.Property<string?>(row, "Description"))).SingleOrDefaultAsync(checkedToken);
                    if (folder is null) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                    var endpoint = new LinkObjectReference(WorkspacePositions.FolderModel, id);
                    if (!await reader.IsEndpointReadableAsync("workspace.content", endpoint, checkedToken)
                        || !await FolderCopyPurposeAllowedAsync(endpoint, checkedToken))
                        throw new LinkQueryException(403, "Workspace.FolderCopyUnavailable");
                    if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => EF.Property<string>(row, "RootFolderId") == id, checkedToken))
                        throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                    nodes.Add(id, folder);
                    // 名称相等由真实数据库collation决定，不能用应用字符串比较替代。
                    if (await Links.Where(row => EF.Property<string>(row, "PEID") == id && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel)
                        .Join(Folders, link => EF.Property<string>(link, "CEID"), child => EF.Property<string>(child, "EID"), (link, child) => EF.Property<string>(child, "ENAME"))
                        .GroupBy(value => value).AnyAsync(group => group.Count() > 1, checkedToken)) throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                    string? last = null;
                    while (true)
                    {
                        var query = Links.AsNoTracking().Where(row => EF.Property<string>(row, "PEID") == id && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel);
                        if (last is not null) query = query.Where(row => string.Compare(EF.Property<string>(row, "LID"), last) > 0);
                        var batch = await query.OrderBy(row => EF.Property<string>(row, "LID")).Take(256).Select(row => new WorkspaceStructureEdge(
                            EF.Property<string>(row, "LID"), id, EF.Property<string>(row, "CEID"), EF.Property<long?>(row, "TreeOrdinal"))).ToArrayAsync(checkedToken);
                        foreach (var edge in batch) { edges.Add(edge); if (discovered.Add(edge.Child)) pending.Enqueue(edge.Child); }
                        if (batch.Length < 256) break;
                        last = batch[^1].LinkId;
                    }
                }
                var plan = WorkspaceFolderStructurePlan.Create(source.Id, nodes.Values.ToArray(), edges);
                if (await Links.AnyAsync(link => EF.Property<string>(link, "PEID") == target.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                    && Folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name), checkedToken))
                    throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
                var candidates = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in plan.Folders) candidates.Add(folder.Id, await PrepareFolderCandidateAsync(StringComparer.OrdinalIgnoreCase.Equals(folder.Id, source.Id) ? name : folder.Name, folder.Description, targetPosition.SpaceId, new FolderCreationSource.Copy(), checkedToken));
                // 全部候选及最终图先成立，之后才产生任何持久写入；新图只指向全新EID，目标为源后代也不会成环。
                var entries = new Dictionary<string, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (oldId, candidate) in candidates)
                {
                    entries.Add(oldId, Folders.Add(candidate));
                    await ownershipAudit.RegisterAsync("workspace.folder", (string)candidate["EID"]!, candidate, false, checkedToken);
                }
                // 只在首次Flush前从真实Added图一次捕获；不复制权限允许，后续仍核原映射和当前完整Entry事实。
                var creation = copyParentAuthorization ? new FolderStructureCreationFacts(source.Id, candidates, entries,
                    db.Database.CurrentTransaction ?? throw new ActionDataAccessException(400, "ActionDataAccess.InitializationSourceInvalid"),
                    parentAuthorizations.CaptureCreatedObjects("workspace.folder", candidates.Values.Select(candidate =>
                        new DataExecutionWriteFacts("workspace.folder", (string)candidate["EID"]!, candidate)).ToArray())) : null;
                await transactions.FlushAsync(checkedToken);
                if (copyParentAuthorization)
                {
                    var incoming = plan.Edges.ToLookup(edge => edge.Child, StringComparer.OrdinalIgnoreCase);
                    foreach (var folder in plan.Folders)
                    {
                        string[] parents = StringComparer.OrdinalIgnoreCase.Equals(folder.Id, source.Id)
                            ? target.FolderId == targetPosition.RootFolderId ? [] : new[] { target.FolderId }
                            : incoming[folder.Id].Select(edge => (string)candidates[edge.Parent]["EID"]!).ToArray();
                        if (parents.Length == 0) continue;
                        var candidate = candidates[folder.Id];
                        var initialized = await parentAuthorizations.InitializeFromParentsAsync("workspace.folder", parents, (string)candidate["EID"]!,
                            StructureInitializationSource(folder.Id, parents, incoming[folder.Id].ToArray(), creation!), checkedToken);
                        if (!initialized.Initialized) continue;
                        await referenceAudit.ParentAuthorizationInitializedAsync(initialized.ParentObjectId, candidate, checkedToken);
                        // 后继必须从真实已保存新父读取；Flush不提交，整个结构仍由外层事务统一回滚。
                        await transactions.FlushAsync(checkedToken);
                    }
                    // 外部目标父ACL锁仍在本事务内；新内部父不增加read/edit要求。
                    await ResolveWritableButtonPositionAsync(actor, targetPosition, checkedToken);
                }
                var createdRoot = (string)candidates[source.Id]["EID"]!;
                var rootLink = await RegisterCreatedReferenceAsync(target.FolderId, WorkspacePositions.FolderModel, createdRoot, checkedToken);
                await referenceAudit.CopiedAsync(linkIds[0], sourceParent.FolderId, rootLink, target.FolderId, new(WorkspacePositions.FolderModel, createdRoot), checkedToken);
                foreach (var group in plan.Edges.GroupBy(edge => edge.Parent, StringComparer.OrdinalIgnoreCase))
                {
                    long ordinal = 0;
                    foreach (var edge in group.OrderBy(edge => edge.Ordinal))
                    {
                        var parent = (string)candidates[edge.Parent]["EID"]!; var child = (string)candidates[edge.Child]["EID"]!;
                        var lid = RegisterCopiedReference(parent, new(edge.LinkId, WorkspacePositions.FolderModel, child, edge.Ordinal), ++ordinal, isScopeNeutral: false);
                        await referenceAudit.CopiedAsync(edge.LinkId, edge.Parent, lid, parent, new(WorkspacePositions.FolderModel, child), checkedToken);
                    }
                }
                foreach (var folder in plan.Folders) await referenceAudit.StructureCopiedAsync(folder.Id, (string)candidates[folder.Id]["EID"]!, checkedToken);
                return new(true, false);
            }, cancellation);
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
