using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using ICP.Platform.Modules.Authorization.DTOs;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Xml.Linq;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceReferenceSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "固定详情投影拒绝请求内部来源字段")]
    public async Task FolderDetails_RejectClientFieldSelectionIncludingOriginSpace()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        var folder = await Command(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position = Position(root), values = new { ENAME = "字段保护" } });
        foreach (var route in new[] { "/api/workspace/folders/" + folder.GetProperty("id").GetString(), "/api/workspace/spaces/" + root.GetProperty("spaceId").GetString() + "/root", "/api/workspace/spaces/roots/" + root.GetProperty("rootFolderId").GetString() })
        {
            var normal = await FeatureHttpTestRequests.ReadAsync(admin, entry, route);
            Assert.False(normal.GetProperty("fields").TryGetProperty("OriginSpaceId", out _));
            using var response = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, route + "?fields=OriginSpaceId");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Workspace.InvalidDetailQuery", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "多父汇聚图与27兄弟的候选和Link查询记录真实SQL往返、物化及计划")]
    public async Task ReachableCandidates_KeepConvergingGraphInSqlBeforePaging()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        var personal = await Command(admin, entry, "/api/workspace/spaces/personal/ensure");
        await Command(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position = Position(personal), values = new { ENAME = "真实来源种子" } });
        // 大图仅作数据夹具：复制真实模块正式创建的审计/来源形状；行为断言仍通过普通主体的真实 HTTP 消费者。
        using (var serviceScope = fixture.Services.CreateScope())
        {
            var db = serviceScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var folders = db.Set<Dictionary<string, object?>>("WorkspaceFolder");
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            var folder = await folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ENAME") == "真实来源种子");
            var link = await links.AsNoTracking().SingleAsync();
            void Folder(string id, string name) => folders.Add(new(folder) { ["EID"] = id, ["ENAME"] = name });
            void Edge(string parent, string child) => links.Add(new(link) { ["LID"] = Guid.NewGuid().ToString("N"), ["PEID"] = parent, ["CEID"] = child });
            for (var i = 0; i < 27; i++) { Folder("sibling" + i, "兄弟" + i.ToString("D2")); Edge(root.GetProperty("rootFolderId").GetString()!, "sibling" + i); }
            for (var level = 0; level < 20; level++)
                for (var side = 0; side < 2; side++)
                {
                    var id = "diamond" + level + "-" + side; Folder(id, "汇聚" + level.ToString("D2") + "-" + side);
                    for (var previous = 0; previous < 2; previous++) Edge(level == 0 ? "sibling" + previous : "diamond" + (level - 1) + "-" + previous, id);
                }
            Folder("private-orphan", "私人孤立"); Folder("unreadable-ancestor", "不可读祖先"); Folder("blocked-child", "被祖先阻断");
            Edge(root.GetProperty("rootFolderId").GetString()!, "unreadable-ancestor"); Edge("unreadable-ancestor", "blocked-child");
            await db.SaveChangesAsync();
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CostReader", "成本读取", "cost-reader", "成本读取账号", entry, Point(fixture, "workspace.folder.add"));
        using var reader = actor.Client; await Rule(admin, fixture, "CostUnreadableAncestor", "unreadable-ancestor", "read", "deny");
        string database;
        using (var scope = fixture.Services.CreateScope()) database = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().Database;
        using var observation = new SqlObservation(database);
        observation.Begin();
        var candidates = await FeatureHttpTestRequests.ReadAsync(reader, Point(fixture, "workspace.folder.add"), "/api/workspace/folders/candidates?pageSize=20");
        var candidateCost = observation.End();
        Assert.Equal(67, candidates.GetProperty("total").GetInt32()); Assert.Equal(20, candidates.GetProperty("items").GetArrayLength());
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "27 siblings and 20 two-node converging levels", representedPaths = "at least 2^20", total = 67, materialized = 20, candidateCost }));
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "candidate actual execution plan", plan = await observation.ReplayPlanAsync(fixture) }));
        observation.Begin();
        var list = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root) + "&pageSize=20");
        var listCost = observation.End();
        Assert.Equal(27, list.GetProperty("total").GetInt32()); Assert.Equal(20, list.GetProperty("items").GetArrayLength());
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "public direct Link page", total = 27, materialized = 20, listCost }));
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "Link actual execution plan", plan = await observation.ReplayPlanAsync(fixture) }));
        var last = await FeatureHttpTestRequests.ReadAsync(reader, entry, "/api/workspace/folders/candidates?pageIndex=4&pageSize=20");
        Assert.Equal(7, last.GetProperty("items").GetArrayLength());
        await Missing(reader, entry, "private-orphan"); await Missing(reader, entry, "blocked-child");
        foreach (var field in new[] { "OriginSpaceId", "fields", "queryScope" })
        {
            using var forbidden = await FeatureHttpTestRequests.SendAsync(reader, entry, HttpMethod.Get, "/api/workspace/folders/candidates?" + field + "=OriginSpaceId");
            Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        }
        var injection = await FeatureHttpTestRequests.ReadAsync(reader, entry, "/api/workspace/folders/candidates?keyword=" + Uri.EscapeDataString("'; DROP TABLE WorkspaceFolder;--"));
        Assert.Equal(0, injection.GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "完整跨空间图和并发引用写入保护不变量")]
    public async Task References_RejectHiddenCyclesDuplicatesRootsAndConcurrentSiblingConflicts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create"); var add = Point(fixture, "workspace.folder.add");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        var personal = await Command(admin, entry, "/api/workspace/spaces/personal/ensure");
        async Task<string> New(JsonElement space, string name) => (await Command(admin, create, "/api/workspace/content/folders", new { position = Position(space), values = new { ENAME = name } })).GetProperty("id").GetString()!;
        var a = await New(root, "A"); var b = await New(root, "B"); var hidden = await New(root, "隐藏中间节点");
        var rootList = await FeatureHttpTestRequests.ReadAsync(admin, entry, ListUrl(root));
        object At(string id) => ChildPosition(root, [LinkTo(rootList, id)]);
        async Task<HttpResponseMessage> Add(HttpClient client, object position, string id) => await FeatureHttpTestRequests.SendAsync(client, add, HttpMethod.Post, "/api/workspace/content/references", new { position, target = new { modelKey = "WorkspaceFolder", id } });
        using (var ah = await Add(admin, At(a), hidden)) Assert.Equal(HttpStatusCode.OK, ah.StatusCode);
        using (var hb = await Add(admin, At(hidden), b)) Assert.Equal(HttpStatusCode.OK, hb.StatusCode);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "GraphWriter", "图写入", "graph-writer", "图写入账号", entry, create, add, Point(fixture, "workspace.folder.remove"));
        using var writer = actor.Client;
        await Rule(admin, fixture, "HiddenCycleNode", hidden, "read", "deny");
        using (var cycle = await Add(writer, At(b), a)) await Conflict(cycle, "Workspace.CycleForbidden", "隐藏中间节点");
        using (var self = await Add(writer, At(a), a)) await Conflict(self, "Workspace.CycleForbidden");
        using (var rootTarget = await Add(writer, At(a), root.GetProperty("rootFolderId").GetString()!)) await Conflict(rootTarget, "Workspace.RootReferenceForbidden");
        using (var privateRoot = await Add(writer, At(a), personal.GetProperty("rootFolderId").GetString()!)) Assert.Equal(HttpStatusCode.NotFound, privateRoot.StatusCode);
        using (var repeated = await Add(admin, At(a), hidden)) await Conflict(repeated, "Workspace.DuplicateReference");
        using (var forged = await Add(writer, Position(personal), a)) Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        var x = await New(root, "X"); var y = await New(root, "Y");
        rootList = await FeatureHttpTestRequests.ReadAsync(admin, entry, ListUrl(root));
        var competingCycle = await Task.WhenAll(Add(admin, At(x), y), Add(admin, At(y), x));
        Assert.Single(competingCycle, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(competingCycle, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in competingCycle) response.Dispose();
        var z = await New(personal, "重复竞争目标");
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Add(admin, At(a), z)));
        Assert.Single(duplicates, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in duplicates) { if (response.StatusCode != HttpStatusCode.OK) await Conflict(response, "Workspace.DuplicateReference"); response.Dispose(); }
        var sameName = await New(personal, "创建引用名称竞争");
        var competingName = await Task.WhenAll(Add(admin, At(a), sameName), FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new { position = At(a), values = new { ENAME = "创建引用名称竞争" } }));
        Assert.Single(competingName, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in competingName) { if (response.StatusCode != HttpStatusCode.OK) await Conflict(response, "Workspace.SiblingNameConflict"); response.Dispose(); }
        await Rule(admin, fixture, "ParentEdit", a, "edit", "deny");
        using (var deniedParent = await Add(writer, At(a), b)) Assert.Equal(HttpStatusCode.Forbidden, deniedParent.StatusCode);
        await Rule(admin, fixture, "TargetRead", b, "read", "deny");
        using (var deniedTarget = await Add(writer, At(x), b)) Assert.Equal(HttpStatusCode.NotFound, deniedTarget.StatusCode);
        using var wrongType = await FeatureHttpTestRequests.SendAsync(writer, add, HttpMethod.Post, "/api/workspace/content/references", new { position = At(x), target = new { modelKey = "Other/Folder", id = b } });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "共享后代需至少一条完整可读公共路径且移除只影响指定LID")]
    public async Task SharedDescendants_RequireReadableAncestorsAndKeepOtherIncomingReferences()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create"); var add = Point(fixture, "workspace.folder.add"); var remove = Point(fixture, "workspace.folder.remove");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        var ownerActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PathOwner", "路径来源", "path-owner", "路径来源账号", entry, create, add, remove);
        var readerActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PathReader", "路径读取", "path-reader", "路径读取账号", entry, add, remove);
        using var owner = ownerActor.Client; using var reader = readerActor.Client;
        var personal = await Command(owner, entry, "/api/workspace/spaces/personal/ensure");
        var parent = (await Command(owner, create, "/api/workspace/content/folders", new { position = Position(personal), values = new { ENAME = "共享父" } })).GetProperty("id").GetString()!;
        var privateList = await FeatureHttpTestRequests.ReadAsync(owner, entry, ListUrl(personal));
        var privateLink = LinkTo(privateList, parent);
        var child = (await Command(owner, create, "/api/workspace/content/folders", new { position = ChildPosition(personal, [privateLink]), values = new { ENAME = "共享后代" } })).GetProperty("id").GetString()!;
        await Command(owner, add, "/api/workspace/content/references", new { position = Position(root), target = new { modelKey = "WorkspaceFolder", id = parent } });
        var publicList = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root)); var publicLink = LinkTo(publicList, parent);
        var publicParentPosition = ChildPosition(root, [publicLink]);
        var sharedChildren = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root, parent, [publicLink]));
        var childLink = LinkTo(sharedChildren, child);
        Assert.Equal(child, (await FeatureHttpTestRequests.ReadAsync(reader, entry, "/api/workspace/folders/" + child)).GetProperty("id").GetString());
        using var privatePath = await FeatureHttpTestRequests.SendAsync(reader, entry, HttpMethod.Get, ListUrl(personal, parent, [privateLink]));
        Assert.Equal(HttpStatusCode.NotFound, privatePath.StatusCode);
        await Rule(admin, fixture, "UnreadableSharedAncestor", parent, "read", "deny");
        await Missing(reader, entry, child);
        using var stalePath = await FeatureHttpTestRequests.SendAsync(reader, entry, HttpMethod.Get, ListUrl(root, parent, [publicLink])); Assert.Equal(HttpStatusCode.NotFound, stalePath.StatusCode);
        await Command(owner, add, "/api/workspace/content/references", new { position = Position(root), target = new { modelKey = "WorkspaceFolder", id = child } });
        var visible = await FeatureHttpTestRequests.ReadAsync(reader, add, "/api/workspace/folders/candidates");
        Assert.Equal(child, Assert.Single(visible.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        publicList = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root)); var alternateLink = LinkTo(publicList, child);
        using var removed = await FeatureHttpTestRequests.SendAsync(reader, remove, HttpMethod.Delete, "/api/workspace/content/references/" + alternateLink, new { position = Position(root) });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode); await Missing(reader, entry, child);
        await Rule(admin, fixture, "UnreadableSharedAncestor", parent, "read", "allow");
        var retained = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root, parent, [publicLink]));
        Assert.Equal(childLink, LinkTo(retained, child));
        using var wrongParent = await FeatureHttpTestRequests.SendAsync(owner, remove, HttpMethod.Delete, "/api/workspace/content/references/" + childLink, new { position = Position(root) });
        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        await Rule(admin, fixture, "RemoveTargetRead", child, "read", "deny");
        using var deniedTarget = await FeatureHttpTestRequests.SendAsync(reader, remove, HttpMethod.Delete, "/api/workspace/content/references/" + childLink, new { position = publicParentPosition });
        Assert.Equal(HttpStatusCode.NotFound, deniedTarget.StatusCode);
        await Rule(admin, fixture, "RemoveTargetRead", child, "read", "allow");
        await Rule(admin, fixture, "RemoveParentEdit", parent, "edit", "deny");
        using var deniedParent = await FeatureHttpTestRequests.SendAsync(reader, remove, HttpMethod.Delete, "/api/workspace/content/references/" + childLink, new { position = publicParentPosition });
        Assert.Equal(HttpStatusCode.Forbidden, deniedParent.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式候选在数据库应用来源范围和分页并排除根")]
    public async Task Candidates_ReadOriginFoldersBeforePagingAndExcludeEveryRoot()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        await Command(admin, entry, "/api/workspace/spaces/personal/ensure");
        for (var index = 0; index < 27; index++)
            await Command(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position = Position(root), values = new { ENAME = "候选" + index.ToString("D2") } });
        var candidates = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/workspace/folders/candidates?pageSize=20");
        Assert.Equal(27, candidates.GetProperty("total").GetInt32()); Assert.Equal(20, candidates.GetProperty("items").GetArrayLength());
        var tail = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/workspace/folders/candidates?pageIndex=2&pageSize=20");
        Assert.Equal(7, tail.GetProperty("items").GetArrayLength());
        Assert.Equal(27, (await FeatureHttpTestRequests.ReadAsync(admin, entry, ListUrl(root))).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "私人来源共享到公共路径、撤销及来源内重加由正式API贯通")]
    public async Task SharedFolder_ReadsPublicPathThenRevokesAndCanBeAddedAgainFromOrigin()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create");
        var add = Point(fixture, "workspace.folder.add");
        var remove = Point(fixture, "workspace.folder.remove");
        var root = await Command(admin, entry, "/api/workspace/spaces/public/initialize");
        var a = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FolderOwner", "文件夹来源", "folder-owner", "来源账号", entry, create, add, remove);
        var b = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FolderReader", "共享读取", "folder-reader", "读取账号", entry, add, remove);
        using var owner = a.Client; using var reader = b.Client;
        var personal = await Command(owner, entry, "/api/workspace/spaces/personal/ensure");
        var privatePosition = Position(personal); var publicPosition = Position(root);
        var created = await Command(owner, create, "/api/workspace/content/folders", new { position = privatePosition, values = new { ENAME = "私人来源共享文件夹" } });
        var id = created.GetProperty("id").GetString()!;
        var ownerBefore = await FeatureHttpTestRequests.ReadAsync(owner, entry, ListUrl(personal));
        var descendant = (await Command(owner, create, "/api/workspace/content/folders", new { position = ChildPosition(personal, [LinkTo(ownerBefore, id)]), values = new { ENAME = "共享保留后代" } })).GetProperty("id").GetString()!;
        var candidates = await FeatureHttpTestRequests.ReadAsync(reader, add, "/api/workspace/folders/candidates");
        Assert.Equal(0, candidates.GetProperty("total").GetInt32());
        await Missing(reader, entry, id);
        await Command(owner, add, "/api/workspace/content/references", new { position = publicPosition, target = new { modelKey = "WorkspaceFolder", id } });
        var detail = await FeatureHttpTestRequests.ReadAsync(reader, entry, "/api/workspace/folders/" + id);
        Assert.Equal("私人来源共享文件夹", detail.GetProperty("fields").GetProperty("ENAME").GetString());
        Assert.False(detail.GetProperty("fields").TryGetProperty("OriginSpaceId", out _));
        Assert.Equal(created.GetProperty("fields").GetProperty("UpdatedAt").GetString(), detail.GetProperty("fields").GetProperty("UpdatedAt").GetString());
        candidates = await FeatureHttpTestRequests.ReadAsync(reader, add, "/api/workspace/folders/candidates");
        Assert.Equal(2, candidates.GetProperty("total").GetInt32());
        Assert.Contains(candidates.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetString() == id);
        Assert.Equal(descendant, (await FeatureHttpTestRequests.ReadAsync(reader, entry, "/api/workspace/folders/" + descendant)).GetProperty("id").GetString());
        var list = await FeatureHttpTestRequests.ReadAsync(reader, entry, ListUrl(root));
        var linkId = Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString();
        using var removed = await FeatureHttpTestRequests.SendAsync(owner, remove, HttpMethod.Delete, "/api/workspace/content/references/" + linkId, new { position = publicPosition });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await Missing(reader, entry, id);
        await Missing(reader, entry, descendant);
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(reader, add, "/api/workspace/folders/candidates")).GetProperty("total").GetInt32());
        var ownList = await FeatureHttpTestRequests.ReadAsync(owner, entry, ListUrl(personal));
        var ownLink = Assert.Single(ownList.GetProperty("items").EnumerateArray()).GetProperty("linkId").GetString();
        using var orphaned = await FeatureHttpTestRequests.SendAsync(owner, remove, HttpMethod.Delete, "/api/workspace/content/references/" + ownLink, new { position = privatePosition });
        Assert.Equal(HttpStatusCode.OK, orphaned.StatusCode);
        var originCandidates = await FeatureHttpTestRequests.ReadAsync(owner, add, "/api/workspace/folders/candidates");
        Assert.Equal(2, originCandidates.GetProperty("total").GetInt32());
        Assert.Contains(originCandidates.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetString() == id);
        var retainedDetail = await FeatureHttpTestRequests.ReadAsync(owner, entry, "/api/workspace/folders/" + id);
        Assert.Equal(created.GetProperty("fields").GetProperty("UpdatedAt").GetString(), retainedDetail.GetProperty("fields").GetProperty("UpdatedAt").GetString());
        Assert.Equal(descendant, (await FeatureHttpTestRequests.ReadAsync(owner, entry, "/api/workspace/folders/" + descendant)).GetProperty("id").GetString());
        await Command(owner, add, "/api/workspace/content/references", new { position = privatePosition, target = new { modelKey = "WorkspaceFolder", id } });
        Assert.Equal(id, Assert.Single((await FeatureHttpTestRequests.ReadAsync(owner, entry, ListUrl(personal))).GetProperty("items").EnumerateArray()).GetProperty("associatedObject").GetProperty("id").GetString());
    }

    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == behavior);
    private static object Position(JsonElement root) => new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
    private static object ChildPosition(JsonElement root, string[] linkIds) => new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds };
    private static string LinkTo(JsonElement list, string id) => Assert.Single(list.GetProperty("items").EnumerateArray(), row => row.GetProperty("associatedObject").GetProperty("id").GetString() == id).GetProperty("linkId").GetString()!;
    private static string ListUrl(JsonElement root, string? anchor = null, string[]? linkIds = null) => "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + (anchor ?? root.GetProperty("rootFolderId").GetString()) + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = JsonSerializer.Serialize(linkIds ?? []) }));
    private static async Task Conflict(HttpResponseMessage response, string code, string? hidden = null)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(code, JsonSerializer.Deserialize<JsonElement>(text).GetProperty("code").GetString());
        if (hidden is not null) Assert.DoesNotContain(hidden, text);
    }
    private static async Task Rule(HttpClient admin, BusinessAuditHostFixture fixture, string key, string id, string action, string effect)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, "workspace.folder", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)), [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = effect })], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static async Task<JsonElement> Command(HttpClient client, FeaturePoint point, string url, object? input = null)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, url, input);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Missing(HttpClient client, FeaturePoint point, string id)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Get, "/api/workspace/folders/" + id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal sealed class SqlObservation : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable _all;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly ConcurrentQueue<string> _statistics = new();
        private readonly Stopwatch _clock = new();
        private string? _sql;
        private SqlParameter[] _parameters = [];
        private bool _active;
        private readonly string _database;
        public SqlObservation(string database) { _database = database; _all = DiagnosticListener.AllListeners.Subscribe(this); }
        public void Begin() { _commands.Clear(); _statistics.Clear(); _sql = null; _parameters = []; _clock.Restart(); _active = true; }
        public object End() { _active = false; _clock.Stop(); return new { databaseCommands = _commands.Count, scopeCommands = _commands.Count(kind => kind == "Scope"), elapsedMs = _clock.Elapsed.TotalMilliseconds, sqlStatistics = _statistics.ToArray() }; }
        public void OnNext(DiagnosticListener value) { if (value.Name == "SqlClientDiagnosticListener") _subscriptions.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!_active || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command
                || !string.Equals(command.Connection?.Database, _database, StringComparison.Ordinal)) return;
            var scope = command.CommandText.Contains("DECLARE @folderCandidatesReadable", StringComparison.Ordinal) || command.CommandText.Contains("WITH Relations AS", StringComparison.Ordinal);
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) _commands.Enqueue(scope ? "Scope" : "Other");
            if (!scope || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)) return;
            _sql = command.CommandText; _parameters = command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray();
            if (command.Connection is SqlConnection connection)
            {
                connection.InfoMessage += (_, args) => { if (_active) _statistics.Enqueue(args.Message); };
                command.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON; " + command.CommandText + " SET STATISTICS IO OFF; SET STATISTICS TIME OFF;";
            }
        }
        // 在同一夹具数据库重放只读查询并提取实际计划摘要，不记录参数、连接或完整SQL。
        public async Task<object> ReplayPlanAsync(BusinessAuditHostFixture fixture)
        {
            Assert.NotNull(_sql); using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await using var connection = new SqlConnection(db.Database.GetDbConnection().ConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SET STATISTICS XML ON; " + _sql + " SET STATISTICS XML OFF;";
            command.Parameters.AddRange(_parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
            await using var reader = await command.ExecuteReaderAsync();
            var plans = new List<XDocument>();
            do
            {
                if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                    while (await reader.ReadAsync()) plans.Add(XDocument.Parse(reader.GetString(0)));
            } while (await reader.NextResultAsync());
            Assert.NotEmpty(plans);
            var nodes = plans.SelectMany(plan => plan.Descendants()).ToArray();
            return new { planCount = plans.Count,
                physicalOperators = nodes.Where(node => node.Name.LocalName == "RelOp").GroupBy(node => node.Attribute("PhysicalOp")?.Value).OrderBy(group => group.Key).ToDictionary(group => group.Key ?? "", group => group.Count()),
                actualRows = nodes.Where(node => node.Name.LocalName == "RunTimeCountersPerThread").Select(node => node.Attribute("ActualRows")?.Value).Where(value => value is not null).Distinct().Take(30).ToArray(),
                databaseTables = nodes.Where(node => node.Name.LocalName == "Object" && node.Attribute("Database") is not null).Select(node => node.Attribute("Table")?.Value).Distinct().ToArray() };
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { _active = false; foreach (var subscription in _subscriptions) subscription.Dispose(); _all.Dispose(); }
    }
}
