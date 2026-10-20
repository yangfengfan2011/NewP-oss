using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Authorization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>归属待补齐状态只描述当前真实对象，不能以列表快照替代提交时的拥有者校验。</summary>
public sealed record WorkspaceOwnershipState(bool Missing, string? OwnerId, string? OwnerDepartmentId, string? CreatedBy);

public sealed partial class WorkspaceContents
{
    /// <summary>待补齐列表沿真实位置和Link端点范围读取，两类缺失在数据库Count和分页前OR组合。</summary>
    public async Task<EntityOwnershipPendingPage> PendingOwnershipAsync(string actor, WorkspacePosition position, int page, int pageSize, CancellationToken token)
    {
        var source = OwnershipReadSource();
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        var path = await positions.ResolveAsync(actor, position, token);
        var scope = new Dictionary<string, string?> { ["spaceId"] = position.SpaceId, ["rootFolderId"] = position.RootFolderId,
            ["linkIds"] = System.Text.Json.JsonSerializer.Serialize(position.LinkIds) };
        var result = await reader.QueryWithConditionsAsync("workspace.content", new("forward", new(WorkspacePositions.FolderModel, path.FolderId),
            new(Page: page, PageSize: pageSize, Fields: ["LID", "Name"], QueryScope: scope)),
            [new("OwnerId", "isNull", null, "missingOwnership", "or"), new("OwnerDepartmentId", "isNull", null, "missingOwnership", "or")],
            new LinkReadSource.Entry(new(source, "query")), token);
        return new(result.Items.Select(item => new EntityOwnershipPendingItem(item.LinkId, item.Fields["Name"] as string ?? "")).ToArray(), result.Total, result.Page, result.PageSize);
    }
    // 转交保留完整LID位置、端点读取及根排除；自己的动作要求随后在规范转交状态点检查。
    private async Task<LinkObjectReference[]> ReadOwnershipTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var targets = await ReadOwnershipSelectionAsync(parentId, linkIds, token);
        foreach (var group in targets.GroupBy(target => target.ModelKey))
            await RequireOwnershipReadableTargetsAsync(group.Key, group.Select(target => target.Id).ToArray(), token);
        return targets;
    }

    // 所有目标来自同一父的真实LID，既不接受裸EID，也不从客户端模型标签推导权限对象。
    private async Task<LinkObjectReference[]> ReadOwnershipSelectionAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var targets = new List<LinkObjectReference>();
        foreach (var batch in linkIds.Chunk(100))
        {
            var rows = await Links.AsNoTracking().Where(row => batch.Contains(EF.Property<string>(row, "LID")) && EF.Property<string>(row, "PEID") == parentId)
                .Select(row => new { Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
            if (rows.Length != batch.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            targets.AddRange(rows.Select(row => new LinkObjectReference(row.Model, row.Id)));
        }
        return targets.Distinct().ToArray();
    }

    // 同模型保留原SQL端点范围和全部空间根排除，不在应用内按授权结果过滤待补齐页面。
    private async Task RequireOwnershipReadableTargetsAsync(string model, string[] ids, CancellationToken token)
    {
        var readable = await reader.GetReadableEndpointIdsAsync("workspace.content", model, ids, token);
        if (ids.Any(id => !readable.Contains(id))) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        if (model == WorkspacePositions.FolderModel)
            foreach (var batch in ids.Chunk(500))
                if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => batch.Contains(EF.Property<string>(row, "RootFolderId")), token))
                    throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
    }

    // 待补齐查询固定原独立read来源，不能使用目标按钮或缺声明降级。
    private DataExecutionBindingReference OwnershipReadSource()
    {
        var source = new DataExecutionBindingReference("workspace.folder.initializeOwnership", "workspace.backend", "read");
        executionAccess.Validate([source], "workspace.folder");
        return source;
    }
}
