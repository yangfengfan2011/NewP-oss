using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>归属待补齐状态只描述当前真实对象，不能以列表快照替代提交时的拥有者校验。</summary>
public sealed record WorkspaceOwnershipState(bool Missing, string? OwnerId, string? OwnerDepartmentId, string? CreatedBy);

public sealed partial class WorkspaceContents
{
    /// <summary>待补齐列表沿真实位置和Link端点范围读取，两类缺失在数据库Count和分页前OR组合。</summary>
    public async Task<EntityOwnershipPendingPage> PendingOwnershipAsync(string actor, WorkspacePosition position, int page, int pageSize, CancellationToken token)
    {
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        var path = await positions.ResolveAsync(actor, position, token);
        var scope = new Dictionary<string, string?> { ["spaceId"] = position.SpaceId, ["rootFolderId"] = position.RootFolderId,
            ["linkIds"] = System.Text.Json.JsonSerializer.Serialize(position.LinkIds) };
        var result = await reader.QueryWithConditionsAsync("workspace.content", new("forward", new(WorkspacePositions.FolderModel, path.FolderId),
            new(Page: page, PageSize: pageSize, Fields: ["LID", "Name"], QueryScope: scope)),
            [new("OwnerId", "isNull", null, "missingOwnership", "or"), new("OwnerDepartmentId", "isNull", null, "missingOwnership", "or")], token);
        return new(result.Items.Select(item => new EntityOwnershipPendingItem(item.LinkId, item.Fields["Name"] as string ?? "")).ToArray(), result.Total, result.Page, result.PageSize);
    }
    /// <summary>从真实位置与LID解析对象，缺失归属单独显示；空间根不开放普通对象补齐。</summary>
    public async Task<WorkspaceOwnershipState> ReadOwnershipAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        var target = await ResolveOwnershipTargetAsync(actor, position, linkId, token);
        var row = await db.Set<Dictionary<string, object?>>(target.ModelKey).AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id, token);
        var owner = row.GetValueOrDefault("OwnerId") as string;
        var department = row.GetValueOrDefault("OwnerDepartmentId") as string;
        return new(owner is null || department is null, owner, department, row.GetValueOrDefault("CreatedBy") as string);
    }

    /// <summary>只对完整位置内的缺失归属执行显式补齐；NULL-only规则和审计由公共归属内核保持。</summary>
    public ValueTask<bool> InitializeOwnershipAsync(string actor, WorkspacePosition position, string linkId,
        string? accountId, EntityOwnershipBackfill backfill, CancellationToken token) => transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var target = await ResolveOwnershipTargetAsync(actor, position, linkId, cancellation);
            try { return await backfill.InitializeAsync(target.ModelKey, Resource(target.ModelKey), target.Id, accountId, cancellation); }
            catch (EntityOwnershipException exception) { throw new LinkQueryException(exception.StatusCode, exception.Code); }
        }, token);

    /// <summary>当前许可页或明确选择的真实LID集合一次补齐；全量资格核对后才写入，未知账号留在待人工列表。</summary>
    public ValueTask<EntityOwnershipBatchResult> InitializeOwnershipManyAsync(string actor, WorkspacePosition position, string[] linkIds,
        EntityOwnershipBackfill backfill, CancellationToken token) => transactions.ExecuteAsync<EntityOwnershipBatchResult>(async cancellation =>
        {
            if (linkIds.Length == 0 || linkIds.Any(string.IsNullOrWhiteSpace) || linkIds.Distinct(StringComparer.Ordinal).Count() != linkIds.Length)
                throw new LinkQueryException(400, "EntityOwnership.InvalidSelection");
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await positions.ResolveAsync(actor, position, cancellation);
            var targets = await ResolveOwnershipTargetsAsync(path.FolderId, linkIds, cancellation);
            var changed = 0; var unresolved = 0;
            try
            {
                foreach (var group in targets.Distinct().GroupBy(target => target.ModelKey).OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    var result = await backfill.InitializeManyAsync(group.Key, Resource(group.Key), group.Select(target => target.Id).ToArray(), cancellation);
                    changed += result.Changed; unresolved += result.Unresolved;
                }
            }
            catch (EntityOwnershipException exception) { throw new LinkQueryException(exception.StatusCode, exception.Code); }
            return new(changed, unresolved);
        }, token);

    // 批次先匹配本位置全部LID，再按模型批量复用端点读取范围及根排除；edit仍由既有真实对象裁决逐项决定。
    private async Task<LinkObjectReference[]> ResolveOwnershipTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var targets = new List<LinkObjectReference>();
        foreach (var batch in linkIds.Chunk(100))
        {
            var rows = await Links.AsNoTracking().Where(row => batch.Contains(EF.Property<string>(row, "LID")) && EF.Property<string>(row, "PEID") == parentId)
                .Select(row => new { Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
            if (rows.Length != batch.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            targets.AddRange(rows.Select(row => new LinkObjectReference(row.Model, row.Id)));
        }
        var distinct = targets.Distinct().ToArray();
        foreach (var group in distinct.GroupBy(target => target.ModelKey))
        {
            var resource = Resource(group.Key);
            var ids = group.Select(target => target.Id).ToArray();
            var readable = await reader.GetReadableEndpointIdsAsync("workspace.content", group.Key, ids, token);
            if (ids.Any(id => !readable.Contains(id))) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (group.Key == WorkspacePositions.FolderModel)
                foreach (var batch in ids.Chunk(500))
                    if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => batch.Contains(EF.Property<string>(row, "RootFolderId")), token))
                        throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
            foreach (var id in ids)
                if (!await decisions.IsAllowedAsync(resource, id, "edit", token)) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
        }
        return distinct;
    }

    // 任何读写都从位置重解实际LID，不接受客户端声明的对象模型或EID；私人路径沿既有准入。
    private async Task<LinkObjectReference> ResolveOwnershipTargetAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        ValidateSelection([linkId]);
        var path = await positions.ResolveAsync(actor, position, token);
        return (await ResolveTargetsAsync(path.FolderId, [linkId], "edit", token)).Single();
    }
}
