using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ICP.Platform.Crud.LinkQueries;
using System.Linq.Expressions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceSearchSqlServerHttpApiTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "树的source关系过滤与正式Link列表一致，不以可读目标绕过被隐藏关系")]
    public async Task SourceRelationFilter_HidesTheSamePathsAsTheFormalLinkList()
    {
        var filter = new SourceFilterState();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.RemoveAll<ILinkQuerySourcePolicy>(); services.AddSingleton(filter);
            services.AddScoped<ILinkQuerySourcePolicy, FilteredSourcePolicy>();
            services.RemoveAll<ILinkTreeSourceReadPolicy>(); services.AddScoped<ILinkTreeSourceReadPolicy, FilteredSourcePolicy>();
        }, includeWorkspace: true);
        using var client = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(client, entry);
        var a = await Create(client, fixture, root, "A"); var b = await Create(client, fixture, root, "B");
        var shared = await Create(client, fixture, a, "匹配共享"); await Create(client, fixture, shared, "匹配后代");
        await Add(client, fixture, b, await FolderId(client, entry, shared));
        var lid = (await Children(client, entry, b)).GetProperty("items")[0].GetProperty("linkId").GetString()!;
        filter.HiddenLinkId = lid;
        var queryScope = JsonSerializer.Serialize(new { spaceId = root.SpaceId, rootFolderId = root.RootFolderId, linkIds = JsonSerializer.Serialize(b.LinkIds) });
        var list = await FeatureHttpTestRequests.ReadAsync(client, entry, $"/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId={await FolderId(client, entry, b)}&queryScope={Uri.EscapeDataString(queryScope)}");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        var result = await Search(client, entry, "匹配", 1, 20);
        Assert.Equal(2, result.GetProperty("total").GetInt64());
        Assert.All(result.GetProperty("items").EnumerateArray(), item => Assert.Equal(a.LinkIds[0], item.GetProperty("node").GetProperty("context").GetProperty("linkIds")[0].GetString()));
        var branch = await FeatureHttpTestRequests.ReadAsync(client, entry, "/api/trees/workspace.structure/children?scope=currentUser&nodeKey=" + Uri.EscapeDataString(ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Encode(new(b.SpaceId, b.RootFolderId, b.LinkIds))));
        Assert.Empty(branch.EnumerateArray());
        using var unavailable = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Get, "/api/trees/workspace.structure/path?scope=currentUser&nodeKey=" + Uri.EscapeDataString(ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Encode(new(b.SpaceId, b.RootFolderId, [.. b.LinkIds, lid]))));
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }

    private sealed class SourceFilterState { public string? HiddenLinkId { get; set; } }
    private sealed class FilteredSourcePolicy(SourceFilterState state) : ILinkQuerySourcePolicy, ILinkTreeSourceReadPolicy
    {
        public string Key => "workspace.content";
        public Task ValidateAsync(LinkQueryDefinition definition, LinkQueryRequest request, CancellationToken token) => Task.CompletedTask;
        public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(CancellationToken token) =>
            Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>?>(state.HiddenLinkId is null ? null : row => EF.Property<string>(row, "LID") != state.HiddenLinkId);
        public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(LinkQueryDefinition definition, CancellationToken token) => ReadPredicateAsync(token);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式混合内容及遗留根Part不误作文件夹边，roots与search都读取文件夹图")]
    public async Task MixedLegacyContents_KeepRootChildrenAndFolderSearchReadable()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var client = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Initialize(client, entry);
        var folder = await Create(client, fixture, root, "归组旧文件夹");
        using (var part = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.createPart"), HttpMethod.Post,
            "/api/workspace/content/parts", new { position = folder, values = new { ECODE = "LEGACY-ROOT", ENAME = "根遗留零组件" } })) Assert.Equal(HttpStatusCode.OK, part.StatusCode);
        using (var document = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post,
            "/api/workspace/content/documents", new { position = folder, values = new { ECODE = "MIXED-DOC", ENAME = "文件夹内图文档" } })) Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        // 仅重建规格保留的历史布局；正式新入口依旧拒绝根下新增业务对象。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var legacy = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == "Part");
            Assert.Null(legacy["TreeOrdinal"]); legacy["PEID"] = root.RootFolderId; await db.SaveChangesAsync();
        }
        await Create(client, fixture, root, "复制目标B");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, client, "MixedTree", "混合树", "mixed-tree", "混合树账号", entry);
        using var ordinary = actor.Client;
        await Initialize(ordinary, entry, true);
        var roots = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, "/api/trees/workspace.structure/roots?scope=currentUser");
        var search = await Search(ordinary, entry, "归组旧文件夹", 1, 20);
        Assert.True(roots.EnumerateArray().Single(node => node.GetProperty("context").GetProperty("spaceId").GetString() == root.SpaceId).GetProperty("hasChildren").GetBoolean(), roots.GetRawText());
        Assert.Equal(2, (await Children(ordinary, entry, root)).GetProperty("items").GetArrayLength());
        Assert.Equal(1, search.GetProperty("total").GetInt64());
        Assert.Equal("归组旧文件夹", Assert.Single(search.GetProperty("items").EnumerateArray()).GetProperty("node").GetProperty("label").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "搜索按真实树先序返回全部多父出现路径，分页计数来自同一合法图")]
    public async Task Search_ReturnsEveryOccurrenceInTreePreorderAndRevalidatesTheChosenChain()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var client = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var root = await Initialize(client, entry);
        var first = await Create(client, fixture, root, "命中Z");
        var second = await Create(client, fixture, root, "命中A");
        var shared = await Create(client, fixture, first, "命中共享");
        var leaf = await Create(client, fixture, shared, "命中后代");
        await Add(client, fixture, second, await FolderId(client, entry, shared));
        var secondShared = (await Children(client, entry, second)).GetProperty("items")[0].GetProperty("linkId").GetString()!;
        var alternate = second with { LinkIds = [.. second.LinkIds, secondShared] };
        var alternateLeaf = alternate with { LinkIds = [.. alternate.LinkIds, leaf.LinkIds[^1]] };
        var expected = new[] { first, shared, leaf, second, alternate, alternateLeaf };
        for (var page = 1; page <= 3; page++)
        {
            var result = await Search(client, entry, "命中", page, 2);
            Assert.Equal(6, result.GetProperty("total").GetInt64());
            Assert.Equal(expected.Skip((page - 1) * 2).Take(2).Select(p => string.Join("/", p.LinkIds)),
                result.GetProperty("items").EnumerateArray().Select(row => string.Join("/", row.GetProperty("node").GetProperty("context").GetProperty("linkIds").EnumerateArray().Select(id => id.GetString()))));
        }
        var path = await Path(client, entry, alternateLeaf);
        Assert.Equal(new[] { "公共空间", "命中A", "命中共享", "命中后代" }, path.GetProperty("names").EnumerateArray().Select(name => name.GetString()));
        using var removed = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + secondShared, new { position = second }, WorkspaceClipboardSqlServerHttpApiTests.ClipboardDeclaration(fixture, Point(fixture, "workspace.folder.remove")));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var stale = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Get, PathUrl(alternateLeaf));
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(4, (await Search(client, entry, "命中", 1, 20)).GetProperty("total").GetInt64());
        Assert.Equal("命中后代", (await Path(client, entry, leaf)).GetProperty("names")[3].GetString());
    }

    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) =>
        Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == behavior
            && (behavior != "workspace.folder.remove" || point.ActionScope == "list.rowActions"));

    private static async Task<WorkspacePosition> Initialize(HttpClient client, FeaturePoint entry, bool personal = false)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Post,
            personal ? "/api/workspace/spaces/personal/ensure" : "/api/workspace/spaces/public/initialize");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }

    private static async Task<WorkspacePosition> Create(HttpClient client, BusinessAuditHostFixture fixture, WorkspacePosition position, string name)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = name } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var row = (await Children(client, Point(fixture, "workspace.spaces.access"), position)).GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("associatedObject").GetProperty("id").GetString() == id);
        return position with { LinkIds = [.. position.LinkIds, row.GetProperty("linkId").GetString()!] };
    }

    private static async Task Add(HttpClient client, BusinessAuditHostFixture fixture, WorkspacePosition position, string id)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position, target = new { modelKey = "WorkspaceFolder", id } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<JsonElement> Search(HttpClient client, FeaturePoint entry, string keyword, int page, int size) =>
        FeatureHttpTestRequests.ReadAsync(client, entry, $"/api/trees/workspace.structure/search?scope=currentUser&keyword={Uri.EscapeDataString(keyword)}&page={page}&pageSize={size}");
    private static string PositionQuery(WorkspacePosition position) =>
        $"spaceId={Uri.EscapeDataString(position.SpaceId)}&rootFolderId={Uri.EscapeDataString(position.RootFolderId)}&linkIds={Uri.EscapeDataString(JsonSerializer.Serialize(position.LinkIds))}";
    private static string PathUrl(WorkspacePosition position) => "/api/workspace/content/path?" + PositionQuery(position);
    private static Task<JsonElement> Path(HttpClient client, FeaturePoint entry, WorkspacePosition position) => FeatureHttpTestRequests.ReadAsync(client, entry, PathUrl(position));
    private static Task<JsonElement> Children(HttpClient client, FeaturePoint entry, WorkspacePosition position) => FeatureHttpTestRequests.ReadAsync(client, entry, "/api/workspace/content/children?" + PositionQuery(position));
    private static async Task<string> FolderId(HttpClient client, FeaturePoint entry, WorkspacePosition position) => (await Path(client, entry, position)).GetProperty("folderId").GetString()!;
}
