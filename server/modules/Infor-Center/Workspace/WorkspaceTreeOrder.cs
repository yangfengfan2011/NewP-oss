using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceTreeOrderResult(bool Moved, string? Boundary);

/// <summary>次序只属于父到文件夹的真实关系；列表和对象审计不消费此规则。</summary>
public sealed class WorkspaceTreeOrder(EfPlatformDbContext db, WorkspaceFolderScope scope, IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit, LinkQueryReader reader)
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
