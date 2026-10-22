using System.Data.Common;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceSearchSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "零命中根及同对象多根仍按半开先序区间计数分页")]
    public async Task RootRanges_KeepZeroHitRootsAndDistinctPrefixesWithoutLosingOccurrences()
    {
        var state = new RootState();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton(state); services.AddScoped<WorkspaceTreeRoots>();
            services.RemoveAll<ILinkTreeRootPolicy>(); services.AddScoped<ILinkTreeRootPolicy, TestRoots>();
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Initialize(admin, entry);
        var empty = await Create(admin, fixture, root, "空分支");
        var a = await Create(admin, fixture, root, "第一根"); var b = await Create(admin, fixture, root, "第二根");
        await Create(admin, fixture, a, "命中A"); await Create(admin, fixture, b, "命中B");
        var emptyId = await FolderId(admin, entry, empty); var aId = await FolderId(admin, entry, a); var bId = await FolderId(admin, entry, b);
        state.Roots = [("zero-before", emptyId), ("a", aId), ("zero-middle", emptyId), ("b", bId), ("zero-after", emptyId)];
        for (var page = 1; page <= 3; page++)
        {
            var result = await Search(admin, entry, "命中", page, 1);
            Assert.Equal(2, result.GetProperty("total").GetInt64());
            Assert.Equal(page <= 2 ? 1 : 0, result.GetProperty("items").GetArrayLength());
            if (page <= 2) Assert.StartsWith(page == 1 ? "a:" : "b:", result.GetProperty("items")[0].GetProperty("node").GetProperty("nodeKey").GetString());
        }
        state.Roots = [("first", aId), ("alias", aId)];
        var aliases = await Search(admin, entry, "命中", 1, 20);
        Assert.Equal(2, aliases.GetProperty("total").GetInt64());
        Assert.Equal(2, aliases.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("node").GetProperty("nodeKey").GetString()).Distinct().Count());
        Assert.Equal(new[] { "first", "alias" }, aliases.GetProperty("items").EnumerateArray().Select(row => LinkTreeNodeKey.Decode(row.GetProperty("node").GetProperty("nodeKey").GetString()!).RootKey));
        var longKey = new string('根', 100); state.Roots = [(longKey, aId)];
        var longRoot = await Search(admin, entry, "命中", 1, 1);
        Assert.Equal(longKey, LinkTreeNodeKey.Decode(longRoot.GetProperty("items")[0].GetProperty("node").GetProperty("nodeKey").GetString()!).RootKey);
        state.Roots = [("outer", root.RootFolderId), ("inner", aId)];
        var overlapping = await Search(admin, entry, "第一根", 1, 20);
        Assert.Equal(1, overlapping.GetProperty("total").GetInt64());
        var ordinary = LinkTreeNodeKey.Decode(Assert.Single(overlapping.GetProperty("items").EnumerateArray()).GetProperty("node").GetProperty("nodeKey").GetString()!);
        Assert.Equal("outer", ordinary.RootKey); Assert.Equal(a.LinkIds, ordinary.LinkIds);
        var overlappingChildren = await Search(admin, entry, "命中", 1, 20);
        Assert.Equal(3, overlappingChildren.GetProperty("total").GetInt64());
        Assert.Equal(new[] { "outer", "outer", "inner" }, overlappingChildren.GetProperty("items").EnumerateArray().Select(item => LinkTreeNodeKey.Decode(item.GetProperty("node").GetProperty("nodeKey").GetString()!).RootKey));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同一源读取管道交集正式data read、Customizer及关系策略，缺失树支持失败关闭")]
    public async Task SourceRows_IntersectReadPlanCustomizerAndPolicyInSql_AndRejectMissingSupport()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var a = await Create(admin, fixture, root, "数据范围排除"); var b = await Create(admin, fixture, root, "Customizer排除");
        var c = await Create(admin, fixture, root, "源策略排除"); var d = await Create(admin, fixture, root, "保留");
        using var scope = fixture.Services.CreateScope(); var services = scope.ServiceProvider;
        var definition = services.GetRequiredService<LinkQueryCatalog>().Get("workspace.content") with { HasDataScopeResource = true };
        var descriptor = services.GetRequiredService<IStandardCrudResourceRegistry>().GetAll().First() with { ResourceKey = "workspace.content", ModelName = "WorkspaceFolderContent", IdField = "LID" };
        var reads = new TestReadPlan(a.LinkIds[0]); var sourcePolicy = new FilteredSourcePolicy(new() { HiddenLinkId = c.LinkIds[0] });
        var rows = new LinkSourceReadScopes(services.GetRequiredService<EfPlatformDbContext>(), reads, new TestRegistry(descriptor), [new TestCustomizer([a.LinkIds[0], c.LinkIds[0], d.LinkIds[0]])], [sourcePolicy]);
        var tree = await rows.TreeRowsAsync(definition, default);
        var ordinary = await rows.RowsAsync(definition, await sourcePolicy.ReadPredicateAsync(default), default);
        Assert.Equal(new[] { d.LinkIds[0] }, await tree.Select(row => EF.Property<string>(row, "LID")).ToArrayAsync());
        Assert.Equal(await ordinary.Select(row => EF.Property<string>(row, "LID")).ToArrayAsync(), await tree.Select(row => EF.Property<string>(row, "LID")).ToArrayAsync());
        foreach (var policies in new ILinkTreeSourceReadPolicy[][] { [], [sourcePolicy, sourcePolicy] })
        {
            var invalid = new LinkSourceReadScopes(services.GetRequiredService<EfPlatformDbContext>(), reads, new TestRegistry(descriptor), [], policies);
            Assert.Equal("LINK_TREE_SOURCE_READ_POLICY_MISSING_OR_DUPLICATE", Assert.Throws<InvalidOperationException>(() => invalid.ValidateTreeSupport(definition)).Message);
        }
        var reader = services.GetRequiredService<LinkTreeReader>(); var view = services.GetRequiredService<LinkTreeCatalog>().Get("workspace.structure");
        foreach (var source in new[] { view.Source with { RootPolicyKey = "missing" }, view.Source with { OrderField = "ENAME" }, view.Source with { LabelColumn = "missing" }, view.Source with { ModelKey = "Part" } })
            Assert.Throws<InvalidOperationException>(() => reader.Validate(view with { Source = source }));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "搜索在空间准入及每层读取之后计数，失效精确链不重定位到另一父链")]
    public async Task Search_PrunesUnreadableAncestorsTargetsAndRootsBeforeCounting()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var personal = await Initialize(admin, entry, true); await Create(admin, fixture, personal, "匹配私人");
        var a = await Create(admin, fixture, root, "A"); var b = await Create(admin, fixture, root, "B");
        var shared = await Create(admin, fixture, a, "匹配共享"); var leaf = await Create(admin, fixture, shared, "匹配后代");
        await Add(admin, fixture, b, await FolderId(admin, entry, shared));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "SearchRead", "搜索读取", "search-read", "搜索账号", entry);
        using var client = actor.Client; var own = await Initialize(client, entry, true);
        Assert.Equal(4, (await Search(client, entry, "匹配", 1, 20)).GetProperty("total").GetInt64());
        await SearchRule(admin, fixture, "SearchAncestor", await FolderId(admin, entry, a), "deny");
        var onlyB = await Search(client, entry, "匹配", 1, 1); Assert.Equal(2, onlyB.GetProperty("total").GetInt64());
        Assert.Equal(b.LinkIds[0], onlyB.GetProperty("items")[0].GetProperty("node").GetProperty("context").GetProperty("linkIds")[0].GetString());
        using (var hiddenChain = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Get, TreePathUrl(leaf))) Assert.Equal(HttpStatusCode.NotFound, hiddenChain.StatusCode);
        using (var privateChain = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Get, TreePathUrl(personal))) Assert.Equal(HttpStatusCode.NotFound, privateChain.StatusCode);
        await SearchRule(admin, fixture, "SearchTarget", await FolderId(admin, entry, shared), "deny");
        Assert.Equal(0, (await Search(client, entry, "匹配", 1, 20)).GetProperty("total").GetInt64());
        await SearchRule(admin, fixture, "SearchTarget", await FolderId(admin, entry, shared), "allow");
        await SearchRule(admin, fixture, "SearchRoot", root.RootFolderId, "deny");
        Assert.Equal(0, (await Search(client, entry, "匹配", 1, 20)).GetProperty("total").GetInt64());
        var roots = await FeatureHttpTestRequests.ReadAsync(client, entry, "/api/trees/workspace.structure/roots?scope=currentUser");
        Assert.Equal(own.RootFolderId, Assert.Single(roots.EnumerateArray()).GetProperty("context").GetProperty("id").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "分页、重复参数、Unicode与字面通配符具有明确合同，损坏或循环明确失败")]
    public async Task Search_ValidatesInputsAndFailsExplicitlyForDamagedGraphs()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var a = await Create(admin, fixture, root, "中文😀%_[甲]~Résumé"); var b = await Create(admin, fixture, a, "后代"); await Create(admin, fixture, root, "近似😁不应匹配");
        foreach (var word in new[] { "😀", "😁", "%_", "[甲]", "~", "résumé", "RÉSUMÉ", " 中文😀%_[甲]~Résumé " })
        {
            var result = await Search(admin, entry, word, 1, 20);
            Assert.True(result.GetProperty("total").GetInt64() == 1, "Literal: " + word + "; actual: " + result.GetRawText());
        }
        Assert.Equal(0, (await Search(admin, entry, "Resume", 1, 20)).GetProperty("total").GetInt64());
        Assert.Equal(0, (await Search(admin, entry, " ", 1, 100)).GetProperty("total").GetInt64());
        Assert.Equal(0, (await Search(admin, entry, new string('界', 256), int.MaxValue, 100)).GetProperty("total").GetInt64());
        var defaults = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/trees/workspace.structure/search?scope=currentUser&keyword=后代");
        Assert.Equal(1, defaults.GetProperty("page").GetInt32()); Assert.Equal(20, defaults.GetProperty("pageSize").GetInt32());
        foreach (var query in new[] { "keyword=a&keyword=b", "keyword=a&page=0", "keyword=a&page=-1", "keyword=a&page=1.0", "keyword=a&pageSize=0", "keyword=a&pageSize=101", "keyword=a&page=1&page=2", "keyword=a&actor=other", "keyword=" + Uri.EscapeDataString(new string('界', 257)) })
        {
            using var invalid = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, "/api/trees/workspace.structure/search?scope=currentUser&" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var wrongScope = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, "/api/trees/workspace.structure/search?scope=global&keyword=后代")) Assert.Equal(HttpStatusCode.BadRequest, wrongScope.StatusCode);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var lid = b.LinkIds[^1];
        var edge = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "LID") == lid);
        var ordinal = edge["TreeOrdinal"]; edge["TreeOrdinal"] = null; await db.SaveChangesAsync(); await SearchError(admin, entry, HttpStatusCode.Conflict, "Platform.TreeGraphInvalid");
        edge["TreeOrdinal"] = ordinal; var child = edge["CEID"]; edge["CEID"] = "missing-folder"; await db.SaveChangesAsync(); await SearchError(admin, entry, HttpStatusCode.Conflict, "Platform.TreeGraphInvalid");
        edge["CEID"] = child; edge["ChildModelKey"] = "unknown-model"; await db.SaveChangesAsync(); await SearchError(admin, entry, HttpStatusCode.Conflict, "Platform.TreeGraphInvalid");
        edge["ChildModelKey"] = "WorkspaceFolder"; edge["CEID"] = root.RootFolderId; await db.SaveChangesAsync(); await SearchError(admin, entry, HttpStatusCode.Conflict, "Platform.TreeGraphInvalid");
    }

    private sealed class RootState { public (string Key, string Id)[]? Roots { get; set; } }
    private sealed class TestRoots(WorkspaceTreeRoots original, RootState state) : ILinkTreeRootPolicy
    {
        public string Key => "workspace.spaces"; public string ScopeKind => "currentUser";
        public Task<LinkTreeRootSql> BuildAsync(LinkTreeDefinition definition, CancellationToken token)
        {
            if (state.Roots is null) return original.BuildAsync(definition, token);
            IReadOnlyList<DbParameter> parameters = [new SqlParameter("@testRoots", JsonSerializer.Serialize(state.Roots.Select((root, index) => new { root.Key, root.Id, Ordinal = index })))];
            return Task.FromResult(new LinkTreeRootSql("", "SELECT [Key] RootKey,Id RootObjectId,[Key] Label,CAST(NULL AS nvarchar(200)) LabelKey,Ordinal RootOrder FROM OPENJSON(@testRoots) WITH([Key] nvarchar(200),Id nvarchar(200),Ordinal int)", parameters));
        }
        public IReadOnlyDictionary<string, object?> CreateContext(string key, string id) => original.CreateContext(key, id);
    }
    private sealed class TestReadPlan(string hidden) : IDataScopeReadPlanProvider
    {
        public Task<DataScopeReadPlan> GetReadPlanAsync(string key, CancellationToken token = default) => Task.FromResult(new DataScopeReadPlan(row => EF.Property<string>(row, "LID") != hidden));
    }
    private sealed class TestRegistry(StandardCrudDescriptor descriptor) : IStandardCrudResourceRegistry
    {
        public IReadOnlyList<StandardCrudDescriptor> GetAll() => [descriptor];
        public StandardCrudDescriptor? FindByResourceKey(string key) => key == descriptor.ResourceKey ? descriptor : null;
        public StandardCrudDescriptor? FindByRoute(string route) => null;
    }
    private sealed class TestCustomizer(string[] allowed) : IStandardCrudQueryCustomizer
    {
        public string ResourceKey => "workspace.content";
        public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query) => allowed.Select(id => new StandardCrudQueryCondition("LID", "=", id, "allowed", "or")).ToArray();
    }
    private static string TreePathUrl(WorkspacePosition position) => "/api/trees/workspace.structure/path?scope=currentUser&nodeKey=" + Uri.EscapeDataString(LinkTreeNodeKey.Encode(new(position.SpaceId, position.RootFolderId, position.LinkIds)));
    private static async Task SearchError(HttpClient client, FeaturePoint entry, HttpStatusCode status, string code)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Get, "/api/trees/workspace.structure/search?scope=currentUser&keyword=后代");
        Assert.Equal(status, response.StatusCode); Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    private static async Task SearchRule(HttpClient admin, BusinessAuditHostFixture fixture, string key, string id, string effect)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, "workspace.folder", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)), [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = effect })], []);
        using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
