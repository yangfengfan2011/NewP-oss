using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceTreeOrderResult(bool Moved, string? Boundary);

/// <summary>次序只属于父到文件夹的真实关系；列表和对象审计不消费此规则。</summary>
public sealed class WorkspaceTreeOrder(EfPlatformDbContext db, WorkspaceFolderScope scope, IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit, LinkQueryCatalog catalog, LinkQueryReader reader)
{
    private DbSet<Dictionary<string, object?>> Links => db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);

    // 所有现有及后续新增引用入口在同一图锁事务内消费；未保存的同批关系也参与末尾计算。
    internal async Task<long> NextOrdinalAsync(string parentId, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace tree append requires the graph transaction.");
        var siblings = Links.Where(row => EF.Property<string>(row, "PEID") == parentId && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel);
        if (await siblings.AnyAsync(row => EF.Property<long?>(row, "TreeOrdinal") == null, token))
            throw new LinkQueryException(409, "Workspace.TreeOrderUnavailable");
        var maximum = await siblings.MaxAsync(row => EF.Property<long?>(row, "TreeOrdinal"), token) ?? 0;
        foreach (var entry in db.ChangeTracker.Entries<Dictionary<string, object?>>().Where(entry => entry.Metadata.Name == WorkspacePositions.ContentModel && entry.State == EntityState.Added))
            if ((string)entry.Entity["PEID"]! == parentId && (string)entry.Entity["ChildModelKey"]! == WorkspacePositions.FolderModel
                && entry.Entity.GetValueOrDefault("TreeOrdinal") is long ordinal) maximum = Math.Max(maximum, ordinal);
        if (maximum == long.MaxValue) throw new LinkQueryException(409, "Workspace.TreeOrderExhausted");
        return maximum + 1;
    }

    /// <summary>读取直属可读文件夹，次序、子存在性在数据库完成；不读列表条件或暴露内部序号。</summary>
    public async Task<LinkQueryResult> ReadChildrenAsync(string actorId, string parentId, CancellationToken token)
    {
        var readable = await reader.ReadEndpointScopeAsync("workspace.content", WorkspacePositions.FolderModel, "treeRead", token);
        var link = db.Model.FindEntityType(WorkspacePositions.ContentModel)!;
        var folder = db.Model.FindEntityType(WorkspacePositions.FolderModel)!;
        var table = WorkspaceFolderScope.Table(link);
        var allowed = catalog.Get("workspace.content").Model.LinkedTypes.Where(pair => pair.Parent == WorkspacePositions.FolderModel).Select(pair => pair.Child).Distinct().ToArray();
        var parameters = readable.Parameters.ToList();
        parameters.Add(new SqlParameter("@treeParent", parentId));
        for (var index = 0; index < allowed.Length; index++) parameters.Add(new SqlParameter("@treeType" + index, allowed[index]));
        var types = string.Join(",", Enumerable.Range(0, allowed.Length).Select(index => "@treeType" + index));
        var sql = readable.Preparation + $"""
            SELECT 1 WHERE @treeParent IN ({readable.IdsQuery});
            SELECT TOP(1) 1 FROM {table} WHERE {WorkspaceFolderScope.Column(link, "PEID")}=@treeParent
                AND {WorkspaceFolderScope.Column(link, "ChildModelKey")} NOT IN ({types});
            WITH Readable AS (SELECT EID FROM ({readable.IdsQuery}) ids(EID))
            SELECT l.{WorkspaceFolderScope.Column(link, "LID")}, f.{WorkspaceFolderScope.Column(folder, "EID")},
                f.{WorkspaceFolderScope.Column(folder, "ENAME")}, l.{WorkspaceFolderScope.Column(link, "TreeOrdinal")},
                CAST(CASE WHEN EXISTS(SELECT 1 FROM {table} child
                    JOIN Readable childRead ON childRead.EID=child.{WorkspaceFolderScope.Column(link, "CEID")}
                    WHERE child.{WorkspaceFolderScope.Column(link, "PEID")}=f.{WorkspaceFolderScope.Column(folder, "EID")}
                    AND child.{WorkspaceFolderScope.Column(link, "ChildModelKey")}=N'WorkspaceFolder') THEN 1 ELSE 0 END AS bit)
            FROM {table} l JOIN {WorkspaceFolderScope.Table(folder)} f ON f.{WorkspaceFolderScope.Column(folder, "EID")}=l.{WorkspaceFolderScope.Column(link, "CEID")}
            JOIN Readable r ON r.EID=f.{WorkspaceFolderScope.Column(folder, "EID")}
            WHERE l.{WorkspaceFolderScope.Column(link, "PEID")}=@treeParent AND l.{WorkspaceFolderScope.Column(link, "ChildModelKey")}=N'WorkspaceFolder'
            ORDER BY l.{WorkspaceFolderScope.Column(link, "TreeOrdinal")},l.{WorkspaceFolderScope.Column(link, "LID")};
            """;
        return await scope.ExecuteAsync(sql, parameters, async reader =>
        {
            if (!await reader.ReadAsync(token)) throw new LinkQueryException(404, "LinkQuery.AnchorUnavailable");
            await reader.NextResultAsync(token);
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("LINK_QUERY_STORED_TYPE_INVALID");
            await reader.NextResultAsync(token);
            var rows = new List<LinkQueryRow>();
            while (await reader.ReadAsync(token))
            {
                if (reader.IsDBNull(3)) throw new LinkQueryException(409, "Workspace.TreeOrderUnavailable");
                var child = new LinkObjectReference(WorkspacePositions.FolderModel, reader.GetString(1));
                rows.Add(new("workspace.content", reader.GetString(0), new(WorkspacePositions.FolderModel, parentId), child, child,
                    new Dictionary<string, object?> { ["LID"] = reader.GetString(0), ["Name"] = reader.GetString(2) }, reader.GetBoolean(4)));
            }
            return new LinkQueryResult(rows, rows.Count, 1, rows.Count, null, null);
        }, token);
    }

    // 调用方已在图锁内验证完整父位置和父 edit；只交换提交时最新的两个可读关系位置。
    internal async Task<WorkspaceTreeOrderResult> ReorderAsync(string actorId, string parentId, string linkId, bool up, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace tree reorder requires the graph transaction.");
        var selected = await Links.SingleOrDefaultAsync(row => EF.Property<string>(row, "LID") == linkId
            && EF.Property<string>(row, "PEID") == parentId && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel, token);
        if (selected is null)
            throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var readable = await reader.ReadEndpointScopeAsync("workspace.content", WorkspacePositions.FolderModel, "treeNeighbor", token);
        var available = await scope.ExecuteAsync(readable.Preparation + $"SELECT 1 WHERE @treeParent IN ({readable.IdsQuery}) AND @treeSelected IN ({readable.IdsQuery});",
            [.. readable.Parameters, new SqlParameter("@treeParent", parentId), new SqlParameter("@treeSelected", (string)selected["CEID"]!)],
            async result => await result.ReadAsync(token), token);
        if (!available) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => EF.Property<string>(row, "RootFolderId") == (string)selected["CEID"]!, token))
            throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
        if (selected.GetValueOrDefault("TreeOrdinal") is not long ordinal) throw new LinkQueryException(409, "Workspace.TreeOrderUnavailable");
        var link = db.Model.FindEntityType(WorkspacePositions.ContentModel)!;
        var comparison = up ? "<" : ">"; var direction = up ? "DESC" : "ASC";
        var sql = readable.Preparation + $"""
            SELECT TOP(1) l.{WorkspaceFolderScope.Column(link, "LID")}
            FROM {WorkspaceFolderScope.Table(link)} l
            WHERE l.{WorkspaceFolderScope.Column(link, "PEID")}=@treeParent AND l.{WorkspaceFolderScope.Column(link, "ChildModelKey")}=N'WorkspaceFolder'
              AND l.{WorkspaceFolderScope.Column(link, "CEID")} IN ({readable.IdsQuery})
              AND l.{WorkspaceFolderScope.Column(link, "TreeOrdinal")} {comparison} @treeOrdinal
            ORDER BY l.{WorkspaceFolderScope.Column(link, "TreeOrdinal")} {direction},l.{WorkspaceFolderScope.Column(link, "LID")} {direction};
            """;
        var neighborId = await scope.ExecuteAsync(sql, [.. readable.Parameters, new SqlParameter("@treeParent", parentId), new SqlParameter("@treeOrdinal", ordinal)],
            async reader => await reader.ReadAsync(token) ? reader.GetString(0) : null, token);
        if (neighborId is null) return new(false, up ? "first" : "last");
        var neighbor = await Links.SingleAsync(row => EF.Property<string>(row, "LID") == neighborId, token);
        var neighborOrdinal = (long)neighbor["TreeOrdinal"]!;
        // 唯一索引保护持久值；NULL 仅在同一事务内部暂借位置，其他读取不能观察未提交中间态。
        selected["TreeOrdinal"] = null;
        await transactions.FlushAsync(token);
        neighbor["TreeOrdinal"] = ordinal;
        await transactions.FlushAsync(token);
        selected["TreeOrdinal"] = neighborOrdinal;
        var stamp = audit.Current();
        selected["LUpdatedBy"] = neighbor["LUpdatedBy"] = stamp.ActorId;
        selected["LUpdatedAt"] = neighbor["LUpdatedAt"] = stamp.OccurredAtUtc;
        return new(true, null);
    }
}
