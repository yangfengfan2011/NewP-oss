using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>出现位置是空间、根及有序关系链，不用 EID 或末端 LID 替代路径身份。</summary>
public sealed record WorkspacePosition(string SpaceId, string RootFolderId, IReadOnlyList<string> LinkIds);
public sealed record WorkspacePositionPath(string FolderId, IReadOnlyList<string> FolderIds, IReadOnlyList<string> Names);

/// <summary>批量验证当前链，读取规模为提交路径长度，不枚举所有根路径。</summary>
public sealed class WorkspacePositions(EfPlatformDbContext db, IDataScopeReadPlanProvider reads)
{
    internal const string FolderModel = "Workspace/WorkspaceFolder";
    internal const string ContentModel = "Workspace/WorkspaceFolderContent";

    /// <summary>根归属、空间准入、链衔接与每层读取资格共同决定位置是否有效。</summary>
    public async Task<WorkspacePositionPath> ResolveAsync(string actorId, WorkspacePosition position, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(position.SpaceId) || string.IsNullOrWhiteSpace(position.RootFolderId) || position.LinkIds is null
            || position.LinkIds.Any(string.IsNullOrWhiteSpace) || position.LinkIds.Distinct(StringComparer.Ordinal).Count() != position.LinkIds.Count)
            throw new LinkQueryException(400, "Workspace.InvalidPosition", "position");
        var space = await WorkspaceSpaces.Admitted(db, actorId).SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == position.SpaceId, token);
        var root = space is null ? null : await WorkspaceSpaces.ValidateRootAsync(db, space, token);
        if (root is null || root.RootFolderId != position.RootFolderId) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
        var links = position.LinkIds.Count == 0 ? [] : await db.Set<Dictionary<string, object?>>(ContentModel).AsNoTracking()
            .Where(row => position.LinkIds.Contains(EF.Property<string>(row, "LID")))
            .Select(row => new { Id = EF.Property<string>(row, "LID"), Parent = EF.Property<string>(row, "PEID"),
                Child = EF.Property<string>(row, "CEID"), Type = EF.Property<string>(row, "ChildModelKey") }).ToArrayAsync(token);
        var byId = links.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var ids = new List<string> { root.RootFolderId };
        foreach (var linkId in position.LinkIds)
        {
            if (!byId.TryGetValue(linkId, out var link) || link.Parent != ids[^1] || link.Type != FolderModel)
                throw new LinkQueryException(404, "Workspace.PositionUnavailable");
            if (ids.Contains(link.Child, StringComparer.Ordinal)) throw new LinkQueryException(400, "Workspace.InvalidPosition", "position");
            ids.Add(link.Child);
        }
        var rows = db.Set<Dictionary<string, object?>>(FolderModel).AsNoTracking();
        var read = await reads.GetReadPlanAsync("workspace.folder", token);
        if (read.Predicate is not null) rows = rows.Where(read.Predicate);
        var names = await rows.Where(row => ids.Contains(EF.Property<string>(row, "EID")))
            .Select(row => new { Id = EF.Property<string>(row, "EID"), Name = EF.Property<string>(row, "ENAME") }).ToDictionaryAsync(row => row.Id, row => row.Name, token);
        if (names.Count != ids.Count) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
        return new(ids[^1], ids, ids.Select(id => names[id]).ToArray());
    }

    /// <summary>通用列表范围只接受正式位置三字段，未知字段或非数组链不能降级为裸锚点。</summary>
    public static WorkspacePosition FromScope(IReadOnlyDictionary<string, string?>? scope)
    {
        if (scope is null || scope.Count != 3 || !scope.TryGetValue("spaceId", out var space) || !scope.TryGetValue("rootFolderId", out var root)
            || !scope.TryGetValue("linkIds", out var chain) || space is null || root is null || chain is null)
            throw new LinkQueryException(400, "Workspace.InvalidPosition", "queryScope");
        try { return new(space, root, JsonSerializer.Deserialize<string[]>(chain) ?? throw new JsonException()); }
        catch (JsonException) { throw new LinkQueryException(400, "Workspace.InvalidPosition", "queryScope"); }
    }
}

/// <summary>Workspace 的正向读取源强制要求真实出现位置；公共 Link 内核不认识空间或文件夹规则。</summary>
public sealed class WorkspaceContentQueryPolicy(WorkspacePositions positions, IHttpContextAccessor context) : ILinkQuerySourcePolicy
{
    public string Key => "workspace.content";

    /// <inheritdoc/>
    public async Task ValidateAsync(LinkQueryDefinition definition, LinkQueryRequest request, CancellationToken token)
    {
        if (request.Direction != "forward" || request.Anchor.ModelKey != WorkspacePositions.FolderModel)
            throw new LinkQueryException(400, "Workspace.UnsupportedQueryDirection", "direction");
        var actor = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
        var path = await positions.ResolveAsync(actor, WorkspacePositions.FromScope(request.Query.QueryScope), token);
        if (path.FolderId != request.Anchor.Id) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
    }

    /// <inheritdoc/>
    public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(CancellationToken token) => Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>?>(null);
}
