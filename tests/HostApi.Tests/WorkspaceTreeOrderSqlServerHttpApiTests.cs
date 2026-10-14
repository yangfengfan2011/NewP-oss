using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modules.Authorization.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceTreeOrderSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "文件夹新增沿持久树次序追加，列表仍按自己的查询排序")]
    public async Task NewFolders_AppendToTreeWhileListKeepsItsQueryOrder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        var root = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var position = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        await Create(admin, fixture, position, "Z先创建");
        await Create(admin, fixture, position, "A后创建");
        var children = await Children(admin, entry, position);
        Assert.Equal(new[] { "Z先创建", "A后创建" }, Names(children));
        var list = await List(admin, entry, position, "&sortBy=Name&sortDirection=asc");
        Assert.Equal(new[] { "A后创建", "Z先创建" }, Names(list));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "树换位只修改关系次序，首尾明确无写入，Host 生命周期重启后保持次序")]
    public async Task Reorder_PersistsAcrossHostRestartWithoutChangingObjectsOrListQueries()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var root = await Initialize(admin, entry);
        await Create(admin, fixture, root, "Z"); await Create(admin, fixture, root, "A"); await Create(admin, fixture, root, "M");
        var original = await Children(admin, entry, root);
        var links = original.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()!).ToArray();
        var objects = await Snapshot(fixture, "WorkspaceFolder");
        var audit = await Snapshot(fixture, "BusinessChange");
        var defaultList = await List(admin, entry, root);
        var filteredPage = await List(admin, entry, root, "&sortBy=Name&sortDirection=desc&pageSize=1&Name%3Acontains=A");
        var beforeBoundary = await Snapshot(fixture, "WorkspaceFolderContent");
        var first = await Move(admin, fixture, root, links[0], "up");
        Assert.False(first.GetProperty("moved").GetBoolean()); Assert.Equal("first", first.GetProperty("boundary").GetString());
        Assert.Equal(beforeBoundary, await Snapshot(fixture, "WorkspaceFolderContent"));
        var last = await Move(admin, fixture, root, links[2], "down");
        Assert.False(last.GetProperty("moved").GetBoolean()); Assert.Equal("last", last.GetProperty("boundary").GetString());
        Assert.Equal(beforeBoundary, await Snapshot(fixture, "WorkspaceFolderContent"));
        Assert.True((await Move(admin, fixture, root, links[2], "up")).GetProperty("moved").GetBoolean());
        Assert.Equal(new[] { "Z", "M", "A" }, Names(await Children(admin, entry, root)));
        Assert.Equal(objects, await Snapshot(fixture, "WorkspaceFolder")); Assert.Equal(audit, await Snapshot(fixture, "BusinessChange"));
        Assert.Equal(defaultList.GetRawText(), (await List(admin, entry, root)).GetRawText());
        Assert.Equal(filteredPage.GetRawText(), (await List(admin, entry, root, "&sortBy=Name&sortDirection=desc&pageSize=1&Name%3Acontains=A")).GetRawText());
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var restored = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restored);
        using var login = await restored.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restored);
        Assert.Equal(new[] { "Z", "M", "A" }, Names(await Children(restored, Point(fixture, "workspace.spaces.access"), root)));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父 edit 与可读相邻文件夹决定排序，隐藏关系位置不变，选中文件夹无需 edit")]
    public async Task Reorder_UsesReadableNeighborsAndParentPermissionWithoutSelectedEdit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Initialize(admin, entry);
        await Create(admin, fixture, root, "A"); await Create(admin, fixture, root, "隐藏B"); await Create(admin, fixture, root, "C");
        var all = (await Children(admin, entry, root)).GetProperty("items").EnumerateArray().ToArray();
        var selected = all[2].GetProperty("linkId").GetString()!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TreeOrder", "树次序", "tree-order", "排序账号", entry,
            OrderPoint(fixture, "up"), OrderPoint(fixture, "down"));
        using var ordinary = actor.Client;
        await Rule(admin, fixture, "HiddenSibling", all[1].GetProperty("associatedObject").GetProperty("id").GetString()!, new() { ["read"] = "deny" });
        await Rule(admin, fixture, "SelectedNoEdit", all[2].GetProperty("associatedObject").GetProperty("id").GetString()!, new() { ["edit"] = "deny" });
        var before = await Rows(fixture, "WorkspaceFolderContent");
        Assert.True((await Move(ordinary, fixture, root, selected, "up")).GetProperty("moved").GetBoolean());
        Assert.Equal(new[] { "C", "A" }, Names(await Children(ordinary, entry, root)));
        var after = await Rows(fixture, "WorkspaceFolderContent");
        var hidden = all[1].GetProperty("linkId").GetString();
        Assert.Equal(before.Single(row => (string)row["LID"]! == hidden)["TreeOrdinal"], after.Single(row => (string)row["LID"]! == hidden)["TreeOrdinal"]);
        Assert.All(after.Where(row => (string)row["LID"]! != hidden), row => Assert.Equal(actor.AccountId, row["LUpdatedBy"]));
        await Rule(admin, fixture, "ParentNoEdit", root.RootFolderId, new() { ["edit"] = "deny" });
        using var refused = await FeatureHttpTestRequests.SendAsync(ordinary, OrderPoint(fixture, "down"), HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition = root, linkId = selected, direction = "down" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Workspace.ParentWriteDenied", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(JsonSerializer.Serialize(after), JsonSerializer.Serialize(await Rows(fixture, "WorkspaceFolderContent")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实共享父的关系同步，异父引用独立，create与add都追加到同级末尾")]
    public async Task SharedParentOccurrences_AgreeWhileIndependentIncomingLinksKeepTheirOwnOrder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var p = await Create(admin, fixture, root, "P"); var q = await Create(admin, fixture, root, "Q");
        var rootRows = (await Children(admin, entry, root)).GetProperty("items").EnumerateArray().ToArray();
        var pPosition = root with { LinkIds = [rootRows[0].GetProperty("linkId").GetString()!] };
        var qPosition = root with { LinkIds = [rootRows[1].GetProperty("linkId").GetString()!] };
        var x = await Create(admin, fixture, pPosition, "Z"); var y = await Create(admin, fixture, pPosition, "A");
        await Add(admin, fixture, qPosition, x.GetProperty("id").GetString()!); await Add(admin, fixture, qPosition, y.GetProperty("id").GetString()!);
        await Add(admin, fixture, qPosition, p.GetProperty("id").GetString()!);
        Assert.Equal(new[] { "Z", "A", "P" }, Names(await Children(admin, entry, qPosition)));
        var pRows = (await Children(admin, entry, pPosition)).GetProperty("items").EnumerateArray().ToArray();
        await Move(admin, fixture, pPosition, pRows[1].GetProperty("linkId").GetString()!, "up");
        var alternateP = qPosition with { LinkIds = [.. qPosition.LinkIds,
            (await Children(admin, entry, qPosition)).GetProperty("items")[2].GetProperty("linkId").GetString()!] };
        Assert.Equal(new[] { "A", "Z" }, Names(await Children(admin, entry, alternateP)));
        Assert.Equal(new[] { "Z", "A", "P" }, Names(await Children(admin, entry, qPosition)));
        await Create(admin, fixture, pPosition, "B");
        Assert.Equal(new[] { "A", "Z", "B" }, Names(await Children(admin, entry, alternateP)));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "并发在图锁后按最新次序换位，唯一值保持一致；换位中间失败整批回滚")]
    public async Task Reorder_SerializesConcurrentWritesAndRollsBackTemporaryUniqueIndexValues()
    {
        var fault = new SwapFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        await Create(admin, fixture, root, "A"); await Create(admin, fixture, root, "B"); await Create(admin, fixture, root, "C");
        var link = (await Children(admin, entry, root)).GetProperty("items")[2].GetProperty("linkId").GetString()!;
        await Task.WhenAll(Move(admin, fixture, root, link, "up"), Move(admin, fixture, root, link, "up"));
        Assert.Equal(new[] { "C", "A", "B" }, Names(await Children(admin, entry, root)));
        var before = await Snapshot(fixture, "WorkspaceFolderContent");
        var objects = await Snapshot(fixture, "WorkspaceFolder");
        fault.Enabled = true;
        using var failed = await FeatureHttpTestRequests.SendAsync(admin, OrderPoint(fixture, "down"), HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition = root, linkId = link, direction = "down" });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(2, fault.Writes); fault.Enabled = false;
        Assert.Equal(before, await Snapshot(fixture, "WorkspaceFolderContent")); Assert.Equal(objects, await Snapshot(fixture, "WorkspaceFolder"));
        Assert.Equal(new[] { "C", "A", "B" }, Names(await Children(admin, entry, root)));
    }

    private sealed class SwapFault : DbCommandInterceptor
    {
        internal bool Enabled;
        internal int Writes;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("UPDATE [dbo].[WorkspaceFolderContent]", StringComparison.Ordinal) && ++Writes == 2)
                throw new InvalidOperationException("Injected tree-order swap failure.");
            return ValueTask.FromResult(result);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "排序拒绝根、业务对象、错父、非法多值和越权输入，改名不重排树")]
    public async Task Reorder_RejectsInvalidOrUnreadableReferencesAndRenameKeepsTheirPositions()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var first = await Create(admin, fixture, root, "Z"); await Create(admin, fixture, root, "A");
        var rows = (await Children(admin, entry, root)).GetProperty("items").EnumerateArray().ToArray();
        var linkId = rows[0].GetProperty("linkId").GetString()!;
        foreach (var input in new object[] {
            new { parentPosition = root, linkIds = new[] { linkId }, direction = "up" },
            new { parentPosition = root, linkId, direction = "left" },
            new { parentPosition = root, linkId, direction = "up", ordinal = 1 },
            new { parentPosition = new { root.SpaceId, root.RootFolderId, root.LinkIds, extra = "unknown" }, linkId, direction = "up" }
        })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, OrderPoint(fixture, "up"), HttpMethod.Post, "/api/workspace/content/tree-order", input);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("Workspace.InvalidTreeOrderInput", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using (var duplicate = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/tree-order"))
        {
            duplicate.Headers.Add("X-NewP-Feature", OrderPoint(fixture, "up").Position);
            duplicate.Content = new StringContent($$"""{"parentPosition":{{JsonSerializer.Serialize(root)}},"linkId":"{{linkId}}","direction":"up","direction":"down"}""", System.Text.Encoding.UTF8, "application/json");
            using var response = await admin.SendAsync(duplicate); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var (position, id) in new[] { (root, root.RootFolderId), (root with { LinkIds = [linkId] }, linkId),
            (root with { SpaceId = "wrong-space" }, linkId) })
        {
            using var rejected = await FeatureHttpTestRequests.SendAsync(admin, OrderPoint(fixture, "up"), HttpMethod.Post,
                "/api/workspace/content/tree-order", new { parentPosition = position, linkId = id, direction = "up" });
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
        using (var createdPart = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createPart"), HttpMethod.Post,
            "/api/workspace/content/parts", new { position = root, values = new { ECODE = "ORDER-PART", ENAME = "业务对象" } })) Assert.Equal(HttpStatusCode.OK, createdPart.StatusCode);
        var partLink = (string)(await Rows(fixture, "WorkspaceFolderContent")).Single(row => (string)row["ChildModelKey"]! == "Part")["LID"]!;
        using (var business = await FeatureHttpTestRequests.SendAsync(admin, OrderPoint(fixture, "up"), HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition = root, linkId = partLink, direction = "up" })) Assert.Equal(HttpStatusCode.NotFound, business.StatusCode);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TreeRead", "树读取", "tree-read", "读取账号", entry, OrderPoint(fixture, "up"));
        using var ordinary = actor.Client;
        await Rule(admin, fixture, "SelectedUnreadable", first.GetProperty("id").GetString()!, new() { ["read"] = "deny" });
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, OrderPoint(fixture, "up"), HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition = root, linkId, direction = "up" })) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var before = await Snapshot(fixture, "WorkspaceFolderContent");
        using (var wrongFeature = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition = root, linkId, direction = "up" })) Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        Assert.Equal(before, await Snapshot(fixture, "WorkspaceFolderContent"));
        using (var renamed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
            "/api/workspace/content/" + linkId, new { position = root, values = new { ENAME = "B" } })) Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(new[] { "B", "A" }, Names(await Children(admin, entry, root)));
        Assert.Equal(before, await Snapshot(fixture, "WorkspaceFolderContent"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "内部次序溢出安全拒绝且创建原子回滚，不重排已有关系")]
    public async Task Append_WhenInternalOrdinalIsExhausted_RollsBackTheCreatedEntity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        await Create(admin, fixture, root, "末尾");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(); link["TreeOrdinal"] = long.MaxValue;
            await db.SaveChangesAsync();
        }
        var objects = await Snapshot(fixture, "WorkspaceFolder"); var links = await Snapshot(fixture, "WorkspaceFolderContent");
        using var failed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position = root, values = new { ENAME = "未提交对象" } });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal("Workspace.TreeOrderExhausted", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(objects, await Snapshot(fixture, "WorkspaceFolder")); Assert.Equal(links, await Snapshot(fixture, "WorkspaceFolderContent"));
    }

    private static async Task<Position> Initialize(HttpClient client, FeaturePoint point)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private static FeaturePoint OrderPoint(BusinessAuditHostFixture fixture, string direction) =>
        fixture.FeatureCatalog.Single(point => point.ActionScope == "contextMenu" && point.Key == (direction == "up" ? "moveUp" : "moveDown"));
    private static async Task<JsonElement> Move(HttpClient client, BusinessAuditHostFixture fixture, Position parentPosition, string linkId, string direction)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, OrderPoint(fixture, direction), HttpMethod.Post,
            "/api/workspace/content/tree-order", new { parentPosition, linkId, direction });
        Assert.True(response.IsSuccessStatusCode, response.StatusCode + ": " + (await response.Content.ReadAsStringAsync()).Split('\n')[0]);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Add(HttpClient client, BusinessAuditHostFixture fixture, Position position, string id)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position, target = new { modelKey = "WorkspaceFolder", id } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task Rule(HttpClient client, BusinessAuditHostFixture fixture, string id, string target, Dictionary<string, string> actions)
    {
        var rule = new DataScopeRuleDto(id, id, null, id, true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target)), [new(id + "Subject", "all", null, 0, actions)], []);
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + id, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static async Task<Dictionary<string, object?>[]> Rows(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var rows = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().ToArrayAsync();
        return rows.OrderBy(row => (string)row[model == "WorkspaceFolderContent" ? "LID" : "EID"]!, StringComparer.Ordinal).ToArray();
    }
    private static async Task<string> Snapshot(BusinessAuditHostFixture fixture, string model) => JsonSerializer.Serialize(await Rows(fixture, model));

    private sealed record Position(
        [property: System.Text.Json.Serialization.JsonPropertyName("spaceId")] string SpaceId,
        [property: System.Text.Json.Serialization.JsonPropertyName("rootFolderId")] string RootFolderId,
        [property: System.Text.Json.Serialization.JsonPropertyName("linkIds")] string[] LinkIds);
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) =>
        Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == behavior
            && (behavior != "workspace.folder.editObject" || point.ActionScope == "list.rowActions"));
    private static async Task<JsonElement> Create(HttpClient client, BusinessAuditHostFixture fixture, Position position, string name)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = name } });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Task<JsonElement> Children(HttpClient client, FeaturePoint point, Position position) => FeatureHttpTestRequests.ReadAsync(client, point,
        $"/api/workspace/content/children?spaceId={Uri.EscapeDataString(position.SpaceId)}&rootFolderId={Uri.EscapeDataString(position.RootFolderId)}&linkIds={Uri.EscapeDataString(JsonSerializer.Serialize(position.LinkIds))}");
    private static Task<JsonElement> List(HttpClient client, FeaturePoint point, Position position, string query = "")
    {
        var scope = JsonSerializer.Serialize(new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = JsonSerializer.Serialize(position.LinkIds) });
        return FeatureHttpTestRequests.ReadAsync(client, point,
            $"/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId={Uri.EscapeDataString(position.LinkIds.Length == 0 ? position.RootFolderId : throw new InvalidOperationException())}&queryScope={Uri.EscapeDataString(scope)}{query}");
    }
    private static string[] Names(JsonElement result) => result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("fields").GetProperty("Name").GetString()!).ToArray();
}
