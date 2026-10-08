using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.Planning;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceReverseSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实对象的反向关系保留原始身份并投影父文件夹")]
    public async Task ReverseReferences_ProjectParentsAndKeepForwardIdentity()
    {
        var constraint = new AnchorBusinessConstraint();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            var registration = Assert.Single(services, service => service.ServiceType == typeof(CompiledPersistencePlan));
            var plan = Assert.IsType<CompiledPersistencePlan>(registration.ImplementationInstance); services.Remove(registration);
            services.AddSingleton(plan with { Models = plan.Models.Select(model => model.LinkQuery is not { } query ? model : model with {
                LinkQuery = query with { Endpoints = query.Endpoints.ToDictionary(pair => pair.Key, pair => pair.Key == "Part/Part" ? pair.Value with { ConstraintKey = constraint.Key } : pair.Value) } }).ToArray() });
            services.AddSingleton<ILinkQueryEndpointConstraint>(constraint);
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var root = await Post(admin, entry, "/api/workspace/spaces/public/initialize");
        var position = Position(root);
        var parent = await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position, values = new { ENAME = "直接父文件夹" } });
        var parentId = parent.GetProperty("id").GetString()!;
        var top = await FeatureHttpTestRequests.ReadAsync(admin, entry, Forward(root.GetProperty("rootFolderId").GetString()!, position));
        var parentLink = Assert.Single(top.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString()!;
        var childPosition = Position(root, parentLink);
        var targets = new List<(string Model, string Id)>();
        foreach (var (model, route, create) in new[] { ("Part/Part", "/api/part/parts", "part.part.create"), ("Document/Document", "/api/document/documents", "document.document.create") })
        {
            var created = await Post(admin, Point(fixture, create), route, new { ECODE = "REVERSE", ENAME = "真实对象" });
            var id = created.GetProperty("fields").GetProperty("EID").GetString()!; targets.Add((model, id));
            await Post(admin, Point(fixture, "workspace.folder.add"), "/api/workspace/content/references", new { position = childPosition, target = new { modelKey = model, id } });
        }
        var child = await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position = childPosition, values = new { ENAME = "真实子文件夹" } });
        targets.Add(("Workspace/WorkspaceFolder", child.GetProperty("id").GetString()!));
        // 三个正式模型共享同一主键字面值，反向查询仍必须按规范模型键区分锚点与对应 LID。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var target in targets)
            {
                await db.Set<Dictionary<string, object?>>(target.Model).Where(row => EF.Property<string>(row, "EID") == target.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => EF.Property<string>(row, "EID"), "shared-endpoint-id"));
                await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").Where(row => EF.Property<string>(row, "ChildModelKey") == target.Model && EF.Property<string>(row, "CEID") == target.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => EF.Property<string>(row, "CEID"), "shared-endpoint-id"));
            }
        }
        targets = targets.Select(target => (target.Model, "shared-endpoint-id")).ToList();
        var forward = await FeatureHttpTestRequests.ReadAsync(admin, entry, Forward(parentId, childPosition));
        foreach (var (model, id) in targets)
        {
            var original = Assert.Single(forward.GetProperty("items").EnumerateArray(), item => item.GetProperty("child").GetProperty("id").GetString() == id && item.GetProperty("child").GetProperty("modelKey").GetString() == model);
            var reverse = await FeatureHttpTestRequests.ReadAsync(admin, entry, Reverse(model, id));
            var row = Assert.Single(reverse.GetProperty("items").EnumerateArray());
            Assert.Equal(1, reverse.GetProperty("total").GetInt32());
            foreach (var key in new[] { "linkId", "parent", "child" }) Assert.Equal(original.GetProperty(key).GetRawText(), row.GetProperty(key).GetRawText());
            Assert.Equal(parentId, row.GetProperty("associatedObject").GetProperty("id").GetString());
            Assert.Equal("Workspace/WorkspaceFolder", row.GetProperty("associatedObject").GetProperty("modelKey").GetString());
            Assert.Equal("直接父文件夹", row.GetProperty("fields").GetProperty("Name").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("fields").GetProperty("Code").ValueKind);
            Assert.DoesNotContain("spaceId", reverse.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("breadcrumb", reverse.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        constraint.ExcludedId = "shared-endpoint-id";
        using (var excludedAnchor = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, Reverse("Part/Part", "shared-endpoint-id"))) Assert.Equal(HttpStatusCode.NotFound, excludedAnchor.StatusCode);
        Assert.Equal(1, (await FeatureHttpTestRequests.ReadAsync(admin, entry, Reverse("Document/Document", "shared-endpoint-id"))).GetProperty("total").GetInt32());
        constraint.Fail = true;
        using var failed = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, Reverse("Part/Part", "shared-endpoint-id")); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
    }

    private sealed class AnchorBusinessConstraint : ILinkQueryEndpointConstraint
    {
        public string Key => "test.reverse.anchor";
        public string ModelKey => "Part/Part";
        public string ExcludedId { get; set; } = "unused";
        public bool Fail { get; set; }
        public Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("TEST_REVERSE_ANCHOR_CONSTRAINT_FAILURE");
            Expression<Func<Dictionary<string, object?>, bool>> predicate = row => EF.Property<string>(row, "EID") != ExcludedId;
            return Task.FromResult(predicate);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "反向父范围、可读祖先、私有入向隐藏与撤权在计数分页前交集")]
    public async Task ReverseReferences_IntersectParentScopeAndPermissionsWithoutPrivatePaths()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create"); var add = Point(fixture, "workspace.folder.add"); var remove = Point(fixture, "workspace.folder.remove");
        var root = await Post(admin, entry, "/api/workspace/spaces/public/initialize");
        var personal = await Post(admin, entry, "/api/workspace/spaces/personal/ensure");
        var privateParent = await Post(admin, create, "/api/workspace/content/folders", new { position = Position(personal), values = new { ENAME = "不得泄露的私人父" } });
        var privateId = privateParent.GetProperty("id").GetString()!;
        var personalList = await FeatureHttpTestRequests.ReadAsync(admin, entry, Forward(personal.GetProperty("rootFolderId").GetString()!, Position(personal)));
        var privateLink = Assert.Single(personalList.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString()!;
        var privatePosition = Position(personal, privateLink);
        var shared = await Post(admin, create, "/api/workspace/content/folders", new { position = privatePosition, values = new { ENAME = "公开共享父" } });
        var sharedId = shared.GetProperty("id").GetString()!;
        var privateContents = await FeatureHttpTestRequests.ReadAsync(admin, entry, Forward(privateId, privatePosition));
        var sharedLink = Assert.Single(privateContents.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString()!;
        var sharedPrivatePosition = Position(personal, privateLink, sharedLink);
        await Post(admin, add, "/api/workspace/content/references", new { position = Position(root), target = new { modelKey = "Workspace/WorkspaceFolder", id = sharedId } });
        var target = await Post(admin, Point(fixture, "part.part.create"), "/api/part/parts", new { ECODE = "PRIVATE-REVERSE", ENAME = "独立零组件" });
        var id = target.GetProperty("fields").GetProperty("EID").GetString()!;
        foreach (var position in new[] { privatePosition, sharedPrivatePosition }) await Post(admin, add, "/api/workspace/content/references", new { position, target = new { modelKey = "Part/Part", id } });
        var descendant = await Post(admin, create, "/api/workspace/content/folders", new { position = sharedPrivatePosition, values = new { ENAME = "共享后代" } });
        var descendantId = descendant.GetProperty("id").GetString()!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReverseReader", "反向读取", "reverse-reader", "反向读取账号", entry, Point(fixture, "part.part.read"));
        using var ordinary = actor.Client;
        var own = await FeatureHttpTestRequests.ReadAsync(admin, entry, Reverse("Part/Part", id)); Assert.Equal(2, own.GetProperty("total").GetInt32());
        var publicOnly = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id) + "&pageSize=1");
        Assert.Equal(1, publicOnly.GetProperty("total").GetInt32());
        Assert.Equal(sharedId, Assert.Single(publicOnly.GetProperty("items").EnumerateArray()).GetProperty("associatedObject").GetProperty("id").GetString());
        foreach (var hidden in new[] { privateId, privateLink, personal.GetProperty("spaceId").GetString()!, "不得泄露" }) Assert.DoesNotContain(hidden, publicOnly.GetRawText());
        var incomingShared = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Workspace/WorkspaceFolder", sharedId));
        Assert.Equal(root.GetProperty("rootFolderId").GetString(), Assert.Single(incomingShared.GetProperty("items").EnumerateArray()).GetProperty("parent").GetProperty("id").GetString());
        await Rule(fixture, admin, "ReverseParentDenied", "workspace.folder", sharedId);
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id))).GetProperty("total").GetInt32());
        using (var inaccessibleDescendant = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Reverse("Workspace/WorkspaceFolder", descendantId))) Assert.Equal(HttpStatusCode.NotFound, inaccessibleDescendant.StatusCode);
        await Rule(fixture, admin, "ReverseParentDenied", "workspace.folder", "unused");
        Assert.Equal(1, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Workspace/WorkspaceFolder", descendantId))).GetProperty("total").GetInt32());
        await Rule(fixture, admin, "ReverseAnchorDenied", "part.part", id);
        using (var deniedAnchor = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Reverse("Part/Part", id))) Assert.Equal(HttpStatusCode.NotFound, deniedAnchor.StatusCode);
        await Rule(fixture, admin, "ReverseAnchorDenied", "part.part", "unused");
        var publicList = await FeatureHttpTestRequests.ReadAsync(admin, entry, Forward(root.GetProperty("rootFolderId").GetString()!, Position(root)));
        var publicLink = Assert.Single(publicList.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString()!;
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, remove, HttpMethod.Delete, "/api/workspace/content/references/" + publicLink, new { position = Position(root) })) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id))).GetProperty("total").GetInt32());
        using (var noPublicAnchor = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Reverse("Workspace/WorkspaceFolder", sharedId))) Assert.Equal(HttpStatusCode.NotFound, noPublicAnchor.StatusCode);
        Assert.Equal(2, (await FeatureHttpTestRequests.ReadAsync(admin, entry, Reverse("Part/Part", id))).GetProperty("total").GetInt32());
        var noWorkspace = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "NoWorkspaceReverse", "无空间读取", "no-workspace-reverse", "无空间读取账号", Point(fixture, "part.part.read"));
        using var unqualified = noWorkspace.Client;
        using var refused = await FeatureHttpTestRequests.SendAsync(unqualified, entry, HttpMethod.Get, Reverse("Part/Part", id)); Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "反向父列筛选、稳定SQL分页、窄字段和异常输入")]
    public async Task ReverseReferences_UseParentFiltersStablePagingAndCanonicalDisplay()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Post(admin, entry, "/api/workspace/spaces/public/initialize");
        await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position = Position(root), values = new { ENAME = "真实父种子" } });
        var target = await Post(admin, Point(fixture, "part.part.create"), "/api/part/parts", new { ECODE = "REVERSE-PAGING", ENAME = "子对象不能参与父名称筛选" });
        var id = target.GetProperty("fields").GetProperty("EID").GetString()!;
        string database;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); database = db.Database.GetDbConnection().Database;
            var folders = db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolder");
            var links = db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent");
            var seed = await folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ENAME") == "真实父种子");
            var edge = await links.AsNoTracking().SingleAsync();
            // 同名对象没有共同父引用，保留同级重名不变量；它们从公共来源范围准入。
            for (var index = 0; index < 27; index++)
            {
                var parentId = "reverse-parent-" + index.ToString("D2");
                folders.Add(new(seed) { ["EID"] = parentId, ["ENAME"] = "并列父名称" });
                links.Add(new(edge) { ["LID"] = "reverse-link-" + index.ToString("D2"), ["PEID"] = parentId, ["CEID"] = id, ["ChildModelKey"] = "Part/Part" });
            }
            await db.SaveChangesAsync();
            await folders.Where(row => EF.Property<string>(row, "ENAME") == "并列父名称").ExecuteUpdateAsync(update =>
                update.SetProperty(row => EF.Property<string>(row, "UpdatedBy"), fixture.ActorId)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc)));
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReversePaging", "反向分页", "reverse-paging", "反向分页账号", entry, Point(fixture, "part.part.read"));
        using var ordinary = actor.Client;
        await Rule(fixture, admin, "ReverseExcludedParent", "workspace.folder", "reverse-parent-03");
        var url = Reverse("Part/Part", id) + "&keyword=" + Uri.EscapeDataString("并列父名称") + "&sortBy=Name&sortDirection=asc&pageSize=7&fields=LID,Name";
        var expected = Enumerable.Range(0, 27).Where(index => index != 3).Select(index => "reverse-link-" + index.ToString("D2")).ToArray();
        using var costs = new WorkspaceReferenceSqlServerHttpApiTests.SqlObservation(database);
        costs.Begin(); var first = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, url); var observation = costs.End();
        Assert.Equal(26, first.GetProperty("total").GetInt32());
        Assert.All(first.GetProperty("items").EnumerateArray(), row => Assert.Equal(new[] { "LID", "Name" }, row.GetProperty("fields").EnumerateObject().Select(field => field.Name)));
        for (var page = 1; page <= 4; page++)
        {
            var result = page == 1 ? first : await FeatureHttpTestRequests.ReadAsync(ordinary, entry, url + "&pageIndex=" + page);
            Assert.Equal(expected.Skip((page - 1) * 7).Take(7), result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
        }
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "WorkspaceReverseParentPage", relations = 27, readable = 26, materialized = 7, observation, plan = await costs.ReplayPlanAsync(fixture) }));
        foreach (var ordering in new[] { "", "&sortBy=Code&sortDirection=asc", "&sortBy=UpdatedAt&sortDirection=desc" })
        {
            var sorted = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id) + "&pageSize=7" + ordering);
            Assert.Equal(expected.Take(7), sorted.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
            var fields = sorted.GetProperty("items")[0].GetProperty("fields");
            Assert.Equal(fixture.ActorId, fields.GetProperty("UpdatedBy").GetString()); Assert.Equal("审计管理员", fields.GetProperty("UpdatedBy_DisplayValue").GetString());
            Assert.Equal("2026-10-08T00:00:00Z", fields.GetProperty("UpdatedAt").GetString()); Assert.False(string.IsNullOrWhiteSpace(fields.GetProperty("UpdatedAt_DisplayValue").GetString()));
        }
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id) + "&keyword=" + Uri.EscapeDataString("子对象不能"))).GetProperty("total").GetInt32());
        Assert.Equal(26, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id) + "&types=Workspace%2FWorkspaceFolder")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Reverse("Part/Part", id) + "&keyword=" + Uri.EscapeDataString("'; DROP TABLE WorkspaceFolder;--"))).GetProperty("total").GetInt32());
        foreach (var (suffix, code, field) in new (string, string, string?)[] {
            ("&fields=OriginSpaceId", "PlatformQuery.UnknownField", null), ("&fields=Description", "PlatformQuery.UnknownField", null), ("&fields=ParentId", "PlatformQuery.UnknownField", null),
            ("&sortBy=UpdatedBy_DisplayValue", "PlatformQuery.DisplayValueSortOrFilterUnsupported", null),
            ("&filters=" + Uri.EscapeDataString("{\"OriginSpaceId\":\"private\"}"), "LinkQuery.UnknownConditionField", "filters"),
            ("&types=Part%2FPart", "LinkQuery.InvalidType", "types"), ("&queryScope=" + Uri.EscapeDataString("{\"spaceId\":\"private\"}"), "Workspace.InvalidPosition", "queryScope"),
            ("&Name:gte=x", "LinkQuery.InvalidCondition", "Name"), ("&UpdatedBy_DisplayValue=x", "LinkQuery.UnknownConditionField", "UpdatedBy_DisplayValue") })
        {
            using var invalid = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Reverse("Part/Part", id) + suffix);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code, problem.GetProperty("code").GetString());
            if (field is not null) Assert.Equal(field, Assert.Single(problem.GetProperty("fieldErrors").EnumerateArray()).GetProperty("field").GetString());
        }
        foreach (var (invalidUrl, status, code) in new (string, HttpStatusCode, string?)[] {
            (Reverse("Unknown/Type", id), HttpStatusCode.BadRequest, "LinkQuery.InvalidAnchor"), (Reverse("Part/Part", ""), HttpStatusCode.BadRequest, "LinkQuery.InvalidAnchor"),
            (Reverse("Workspace/WorkspaceFolder", id), HttpStatusCode.NotFound, "LinkQuery.AnchorUnavailable"), (Reverse("Part/Part", "missing"), HttpStatusCode.NotFound, "LinkQuery.AnchorUnavailable"),
            (Reverse("Part/Part", id).Replace("direction=reverse", "direction=wrong"), HttpStatusCode.BadRequest, "LinkQuery.InvalidDirection"),
            (Reverse("Part/Part", id).Replace("workspace.content", "unknown.resource"), HttpStatusCode.NotFound, null) })
        {
            using var invalid = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, invalidUrl);
            Assert.Equal(status, invalid.StatusCode);
            if (code is not null) Assert.Equal(code, (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    private static async Task Rule(BusinessAuditHostFixture fixture, HttpClient admin, string key, string resource, string id)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, resource, new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == behavior);
    private static object Position(JsonElement root, params string[] links) => new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = links };
    private static string Forward(string id, object position)
    {
        var value = JsonSerializer.SerializeToElement(position);
        return "/api/links/workspace.content?direction=forward&anchorModelKey=Workspace%2FWorkspaceFolder&anchorId=" + Uri.EscapeDataString(id) + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = value.GetProperty("spaceId").GetString(), rootFolderId = value.GetProperty("rootFolderId").GetString(), linkIds = value.GetProperty("linkIds").GetRawText() }));
    }
    private static string Reverse(string model, string id) => "/api/links/workspace.content?direction=reverse&anchorModelKey=" + Uri.EscapeDataString(model) + "&anchorId=" + Uri.EscapeDataString(id);
    private static async Task<JsonElement> Post(HttpClient client, FeaturePoint point, string url, object? body = null)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, url, body);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
