using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Query.Models;

namespace ICP.Platform.Modules.Workspace;

/// <summary>筛选应用于完整可读候选，不改变真实图遍历或静默扩展提交集合。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceBatchPermissionFilters(IReadOnlyList<string>? ResourceKeys = null, string? CreatedBy = null, string? OwnerId = null);

/// <summary>每个规范目标只选一个当前可用来源；可读但不可管理仍给出明确安全反馈。</summary>
public sealed record WorkspaceBatchPermissionItem(string ResourceKey, string ObjectId, string Name, string? Code,
    bool Manageable, WorkspaceBatchPermissionSource Source);

/// <summary>资源动作和主体范围来自正式授权目录，名称不充当动作身份。</summary>
public sealed record WorkspaceBatchPermissionCatalog(string ResourceKey, IReadOnlyList<DataScopePermissionAction> Actions,
    IReadOnlyList<DataScopeSubjectType> SubjectTypes);

/// <summary>完整匹配集合以规范对象去重；不返回隐藏路径、隐藏数量或ACL原文。</summary>
public sealed record WorkspaceBatchPermissionCollection(IReadOnlyList<WorkspaceBatchPermissionItem> Items, int Total,
    IReadOnlyList<WorkspaceBatchPermissionCatalog> Catalogs);

public sealed partial class WorkspaceContents
{
    /// <summary>沿真实可读图分层分页并按EID访问一次Folder；共享路径不指数展开，筛选仍由现查询内核下推。</summary>
    public ValueTask<WorkspaceBatchPermissionCollection> PrepareBatchPermissionsAsync(string actor,
        IReadOnlyList<WorkspaceBatchPermissionSource> sources, bool includeDescendants, WorkspaceBatchPermissionFilters? filters,
        IDataScopeObjectPermissionQueries queries, CancellationToken token) =>
        transactions.ExecuteAsync<WorkspaceBatchPermissionCollection>(async cancellation =>
        {
            if (sources is null || sources.Count == 0) throw new LinkQueryException(400, "Workspace.InvalidSelection");
            var knownResources = catalog.Get("workspace.content").Endpoints.Values.Select(endpoint => endpoint.ResourceKey).ToHashSet(StringComparer.Ordinal);
            if (filters?.ResourceKeys?.Any(key => !knownResources.Contains(key)) == true
                || filters?.CreatedBy is { } creator && (string.IsNullOrWhiteSpace(creator) || creator.Length > 128)
                || filters?.OwnerId is { } owner && (string.IsNullOrWhiteSpace(owner) || owner.Length > 128))
                throw new LinkQueryException(400, "Workspace.InvalidSelection");
            // 可空筛选仍先核输入显式引用的真实读取来源；管理资格在各实际候选上独立重验。
            ValidatePermissionSources(WorkspacePermissionExecutions.ViewSources);
            await LockGraphAsync(cancellation);
            var items = new Dictionary<DataScopeAuthorizationTarget, WorkspaceBatchPermissionItem>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<(string Id, WorkspacePosition Position)>();
            foreach (var source in sources)
            {
                if (source is null || source.Position is null || source.LinkIds is null || source.LinkIds.Length == 0
                    || source.LinkIds.Any(string.IsNullOrWhiteSpace)) throw new LinkQueryException(400, "Workspace.InvalidSelection");
                var path = await positions.ResolveAsync(actor, source.Position, cancellation);
                var selected = source.LinkIds.Distinct(StringComparer.Ordinal).ToArray();
                foreach (var segment in selected.Chunk(100))
                    await ResolveTargetsAsync(path.FolderId, segment, cancellation);
                await AddMatchingAsync(path.FolderId, source.Position, selected);
                if (includeDescendants)
                    await DiscoverAsync(path.FolderId, source.Position, selected);
            }
            while (pending.TryDequeue(out var folder))
            {
                cancellation.ThrowIfCancellationRequested();
                await AddMatchingAsync(folder.Id, folder.Position, null);
                await DiscoverAsync(folder.Id, folder.Position, null);
            }
            var ordered = items.Values.OrderBy(item => item.ResourceKey, StringComparer.Ordinal).ThenBy(item => item.ObjectId, StringComparer.Ordinal).ToArray();
            var catalogs = new List<WorkspaceBatchPermissionCatalog>();
            foreach (var group in ordered.GroupBy(item => item.ResourceKey))
            {
                var value = await queries.CatalogAsync(group.Key, group.First().ObjectId, WorkspacePermissionExecutions.ViewSources, cancellation)
                    ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
                catalogs.Add(new(group.Key, value.Actions, value.SubjectTypes));
            }
            return new(ordered, ordered.Length, catalogs);

            // 过滤后只加入正式查询可见的目标，不以私有表或历史名称补全。
            async Task AddMatchingAsync(string parent, WorkspacePosition position, string[]? selected)
            {
                await foreach (var row in ReadBatchPermissionRowsAsync(parent, position, selected, filters, false, cancellation))
                {
                    var key = new DataScopeAuthorizationTarget(Resource(row.AssociatedObject.ModelKey), row.AssociatedObject.Id);
                    if (items.ContainsKey(key)) continue;
                    var status = await objectActions.CheckAsync(row.AssociatedObject.ModelKey, key.ObjectId, ["read", "authorize"], cancellation);
                    if (status == DataExecutionDecision.NotFound) continue;
                    items.Add(key, new(key.ResourceKey, key.ObjectId, row.Fields["Name"] as string ?? "", row.Fields["Code"] as string,
                        status == DataExecutionDecision.Allowed, new(position, [row.LinkId])));
                }
            }

            // 仅成功准入的Folder入队；先遇到不可读路径不能把另一合法路径的对象标成已访问。
            async Task DiscoverAsync(string parent, WorkspacePosition position, string[]? selected)
            {
                await foreach (var row in ReadBatchPermissionRowsAsync(parent, position, selected, null, true, cancellation))
                    if (visited.Add(row.AssociatedObject.Id)) pending.Enqueue((row.AssociatedObject.Id,
                        position with { LinkIds = [.. position.LinkIds, row.LinkId] }));
            }
        }, token);

    // 请求只指定受控业务筛选；固定投影、模型到资源映射及读取范围继续由正式Link目录拥有。
    private async IAsyncEnumerable<LinkQueryRow> ReadBatchPermissionRowsAsync(string parent, WorkspacePosition position,
        string[]? selected, WorkspaceBatchPermissionFilters? filters, bool foldersOnly, [EnumeratorCancellation] CancellationToken token)
    {
        var endpoints = catalog.Get("workspace.content").Endpoints;
        var types = endpoints.Keys.Where(model => foldersOnly ? model == WorkspacePositions.FolderModel
            : filters?.ResourceKeys is null || filters.ResourceKeys.Contains(Resource(model), StringComparer.Ordinal)).ToArray();
        if (types.Length == 0) yield break;
        var conditions = new List<PlatformQueryCondition>();
        if (!foldersOnly && filters?.CreatedBy is { } creator) conditions.Add(new("CreatedBy", "=", creator));
        if (!foldersOnly && filters?.OwnerId is { } owner) conditions.Add(new("OwnerId", "=", owner));
        var scope = new Dictionary<string, string?> { ["spaceId"] = position.SpaceId, ["rootFolderId"] = position.RootFolderId,
            ["linkIds"] = JsonSerializer.Serialize(position.LinkIds) };
        for (var page = 1; ; page++)
        {
            var request = new LinkQueryRequest("forward", new(WorkspacePositions.FolderModel, parent),
                new(Page: page, PageSize: 128, Fields: foldersOnly ? ["LID"] : ["LID", "Name", "Code"], QueryScope: scope), types);
            var result = selected is null ? await reader.QueryWithConditionsAsync("workspace.content", request, conditions, token)
                : await reader.QuerySelectedAsync("workspace.content", request, selected, conditions, token);
            foreach (var row in result.Items) yield return row;
            if ((long)page * result.PageSize >= result.Total) yield break;
        }
    }
}
