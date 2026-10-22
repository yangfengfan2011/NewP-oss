using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>出现位置是空间、根及有序关系链，不用 EID 或末端 LID 替代路径身份。</summary>
public sealed record WorkspacePosition(string SpaceId, string RootFolderId, IReadOnlyList<string> LinkIds);
public sealed record WorkspacePositionPath(string FolderId, IReadOnlyList<string> FolderIds, IReadOnlyList<string> Names);

/// <summary>批量验证当前链，读取规模为提交路径长度，不枚举所有根路径。</summary>
public sealed class WorkspacePositions(LinkTreeReader trees)
{
    internal const string FolderModel = "WorkspaceFolder";
    internal const string ContentModel = "WorkspaceFolderContent";

    /// <summary>根归属、空间准入、链衔接与每层读取资格共同决定位置是否有效。</summary>
    public Task<WorkspacePositionPath> ResolveAsync(string actorId, WorkspacePosition position, CancellationToken token) =>
        ReadPositionAsync(() => trees.PathAsync("workspace.structure", new(position.SpaceId, position.RootFolderId, position.LinkIds), token));

    /// <summary>公开路径API使用服务端固定读取来源，内部位置解析继续走其原显式内部来源。</summary>
    public Task<WorkspacePositionPath> ResolveFromApiAsync(string actorId, WorkspacePosition position,
        DataExecutionApiQueryContext source, CancellationToken token) =>
        ReadPositionAsync(() => trees.PathFromApiAsync("workspace.structure", new(position.SpaceId, position.RootFolderId, position.LinkIds), source, token));

    // 两条已固定来源的调用保留相同读取、结果及错误映射，不另枚举替代链。
    private static async Task<WorkspacePositionPath> ReadPositionAsync(Func<Task<IReadOnlyList<LinkTreeNode>>> read)
    {
        try
        {
            var path = await read();
            return new((string)path[^1].Context["id"]!, path.Select(node => (string)node.Context["id"]!).ToArray(), path.Select(node => node.Label).ToArray());
        }
        catch (LinkQueryException exception) when (exception.Code == "Platform.TreeInvalidPosition")
        { throw new LinkQueryException(400, "Workspace.InvalidPosition", "position"); }
        catch (LinkQueryException exception) when (exception.Status == 404)
        { throw new LinkQueryException(404, "Workspace.PositionUnavailable"); }
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

/// <summary>正向读取验证出现位置；反向仅读取经过端点范围交集的直接关系，不授予路径跳转。</summary>
public sealed class WorkspaceContentQueryPolicy(WorkspacePositions positions, IHttpContextAccessor context, LinkQueryCatalog catalog,
    IEnumerable<ILinkTreeSourceReadPolicy> reads) : ILinkQuerySourcePolicy
{
    public string Key => "workspace.content";

    /// <inheritdoc/>
    public async Task ValidateAsync(LinkQueryDefinition definition, LinkQueryRequest request, CancellationToken token)
    {
        if (request.Direction == "reverse")
        {
            if (request.Query.QueryScope is { Count: > 0 }) throw new LinkQueryException(400, "Workspace.InvalidPosition", "queryScope");
            return;
        }
        if (request.Direction != "forward" || request.Anchor.ModelKey != WorkspacePositions.FolderModel)
            throw new LinkQueryException(400, "Workspace.UnsupportedQueryDirection", "direction");
        var actor = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
        var path = await positions.ResolveAsync(actor, WorkspacePositions.FromScope(request.Query.QueryScope), token);
        if (path.FolderId != request.Anchor.Id) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
    }

    /// <inheritdoc/>
    public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(CancellationToken token) => reads.Single(read => read.Key == Key).ReadPredicateAsync(catalog.Get(Key), token);
}

/// <summary>Workspace 没有额外关系行过滤；位置验证由正式树内核承担，两个读取入口共享此事实。</summary>
public sealed class WorkspaceContentReadPredicate : ILinkTreeSourceReadPolicy
{
    public string Key => "workspace.content";
    /// <inheritdoc/>
    public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(LinkQueryDefinition definition, CancellationToken token) =>
        Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>?>(null);
}
