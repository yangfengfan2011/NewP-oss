using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>Workspace 拥有位置内的原子写入；通用查询没有任意 Link CRUD。</summary>
public sealed class WorkspaceContents(EfPlatformDbContext db, WorkspacePositions positions, IPlatformTransactionExecutor transactions,
    IDataScopeObjectDecisionProvider decisions, IPlatformObjectIdGenerator ids, IEntityAuditValueProvider audit, LinkQueryCatalog catalog,
    WorkspaceFolderScope folderScope, LinkQueryReader reader)
{
    /// <summary>在有效位置内创建实体与 Link，同父名称比较及并发串行化由 SQL Server 事务锁保护。</summary>
    public ValueTask<string> CreateFolderAsync(string actorId, WorkspacePosition position, IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        if (values.Keys.Any(key => key is not ("ENAME" or "Description"))) throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", values.Keys.First(key => key is not ("ENAME" or "Description")));
        if (!values.TryGetValue("ENAME", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 256)
            throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        var description = values.GetValueOrDefault("Description");
        if (description.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String) || description.ValueKind == JsonValueKind.String && description.GetString()!.Length > 500)
            throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        return transactions.ExecuteAsync<string>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await positions.ResolveAsync(actorId, position, cancellation);
            if (!await decisions.IsAllowedAsync("workspace.folder", path.FolderId, "edit", cancellation)) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
            var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
            var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);
            if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == path.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                && folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name.GetString()), cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            var id = ids.NewId("WorkspaceFolder");
            var candidate = new Dictionary<string, object?> { ["EID"] = id, ["ENAME"] = name.GetString(),
                ["Description"] = description.ValueKind == JsonValueKind.String ? description.GetString() : null, ["OriginSpaceId"] = position.SpaceId };
            audit.ForCreate().ApplyTo(candidate);
            if (!await decisions.IsCandidateAllowedAsync("workspace.folder", candidate, "create", cancellation)) throw new LinkQueryException(403, "Workspace.FolderCreateDenied");
            // 实际端点及配对必须通过同一受信目录；新对象没有外部写入其类型的机会。
            var definition = catalog.Get("workspace.content");
            if (!definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == WorkspacePositions.FolderModel)
                || !await folders.AnyAsync(folder => EF.Property<string>(folder, "EID") == path.FolderId, cancellation))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            folders.Add(candidate);
            await transactions.FlushAsync(cancellation);
            var stamp = audit.Current();
            links.Add(new()
            {
                ["LID"] = ids.NewId("WorkspaceFolderContent"), ["PEID"] = path.FolderId, ["CEID"] = id, ["ChildModelKey"] = WorkspacePositions.FolderModel,
                ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
            });
            return id;
        }, token);
    }

    /// <summary>已有对象引用共享真实实体；锁内重验位置、配对与目标读取，文件夹另维护完整图不变量。</summary>
    public ValueTask<bool> AddReferenceAsync(string actorId, WorkspacePosition position, LinkObjectReference target, CancellationToken token) =>
        transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, position, cancellation);
            var definition = catalog.Get("workspace.content");
            if (string.IsNullOrWhiteSpace(target.Id) || !definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == target.ModelKey))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint", "target");
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, cancellation)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceSpace").AnyAsync(space => EF.Property<string>(space, "RootFolderId") == target.Id, cancellation))
                throw new LinkQueryException(409, "Workspace.RootReferenceForbidden");
            var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
            if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == path.FolderId && EF.Property<string>(link, "ChildModelKey") == target.ModelKey
                && EF.Property<string>(link, "CEID") == target.Id, cancellation)) throw new LinkQueryException(409, "Workspace.DuplicateReference");
            if (target.ModelKey == WorkspacePositions.FolderModel)
            {
                var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);
                var name = await folders.Where(folder => EF.Property<string>(folder, "EID") == target.Id).Select(folder => EF.Property<string>(folder, "ENAME")).SingleAsync(cancellation);
                if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == path.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                    && folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name), cancellation))
                    throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
                if (await WouldCycleAsync(path.FolderId, target.Id, cancellation)) throw new LinkQueryException(409, "Workspace.CycleForbidden");
            }
            var stamp = audit.Current();
            links.Add(new()
            {
                ["LID"] = ids.NewId("WorkspaceFolderContent"), ["PEID"] = path.FolderId, ["CEID"] = target.Id, ["ChildModelKey"] = target.ModelKey,
                ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
            });
            return true;
        }, token);

    /// <summary>移除只删除指定 LID；关系当前父端、目标读取与有效出现位置均由提交时事实决定。</summary>
    public ValueTask<bool> RemoveAsync(string actorId, WorkspacePosition position, string linkId, CancellationToken token) =>
        transactions.ExecuteAsync<bool>(async cancellation =>
        {
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, position, cancellation);
            var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
            var link = await links.SingleOrDefaultAsync(row => EF.Property<string>(row, "LID") == linkId && EF.Property<string>(row, "PEID") == path.FolderId, cancellation);
            if (link is null) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            var target = new LinkObjectReference((string)link["ChildModelKey"]!, (string)link["CEID"]!);
            var definition = catalog.Get("workspace.content");
            if (!definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == target.ModelKey)
                || !await reader.IsEndpointReadableAsync("workspace.content", target, cancellation)) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            links.Remove(link);
            return true;
        }, token);

    // 所有内容写入按相同顺序取得跨 Host 的事务级图锁，防止互成环及 create/add 同级名竞争。
    private Task LockGraphAsync(CancellationToken token) => db.Database.ExecuteSqlRawAsync(
        "DECLARE @result int; EXEC @result = sp_getapplock @Resource=N'Workspace.FolderGraph', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace content lock failed.', 1;", token);

    // 锁后重新解析实际位置和父动作，不使用选择候选时或等待锁前的资格。
    private async Task<WorkspacePositionPath> WritablePositionAsync(string actorId, WorkspacePosition position, CancellationToken token)
    {
        var path = await positions.ResolveAsync(actorId, position, token);
        if (!await decisions.IsAllowedAsync("workspace.folder", path.FolderId, "edit", token)) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
        return path;
    }

    // 防环观察完整存储图，包括不可见节点与跨空间关系；只返回布尔值，不泄露名称或路径。
    private async Task<bool> WouldCycleAsync(string parent, string child, CancellationToken token)
    {
        var link = db.Model.FindEntityType(WorkspacePositions.ContentModel)!;
        var idType = db.Model.FindEntityType(WorkspacePositions.FolderModel)!.FindProperty("EID")!.GetColumnType()!;
        var sql = $"""
            SET NOCOUNT ON;
            DECLARE @seen TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE @frontier TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE @next TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT INTO @seen VALUES(@child); INSERT INTO @frontier VALUES(@child);
            WHILE EXISTS(SELECT 1 FROM @frontier) AND NOT EXISTS(SELECT 1 FROM @seen WHERE EID=@parent)
            BEGIN
                INSERT INTO @next SELECT DISTINCT l.{WorkspaceFolderScope.Column(link, "CEID")} FROM {WorkspaceFolderScope.Table(link)} l
                    JOIN @frontier f ON f.EID=l.{WorkspaceFolderScope.Column(link, "PEID")}
                    WHERE l.{WorkspaceFolderScope.Column(link, "ChildModelKey")}=N'Workspace/WorkspaceFolder'
                    AND NOT EXISTS(SELECT 1 FROM @seen v WHERE v.EID=l.{WorkspaceFolderScope.Column(link, "CEID")});
                DELETE FROM @frontier; INSERT INTO @frontier SELECT EID FROM @next;
                INSERT INTO @seen SELECT EID FROM @next; DELETE FROM @next;
            END;
            SELECT 1 WHERE EXISTS(SELECT 1 FROM @seen WHERE EID=@parent);
            """;
        return await folderScope.ExecuteAsync(sql, [new SqlParameter("@parent", parent), new SqlParameter("@child", child)], async reader => await reader.ReadAsync(token), token);
    }
}
