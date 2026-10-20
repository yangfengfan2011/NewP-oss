using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 只复用既有Clipboard数据库/连接/IO/XML与图锁仪器，业务样本使用正式Workspace读取和文件夹入口。
public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [Theory]
    [InlineData("<ShowPlanXML><RelOp PhysicalOp='Constant Scan'/></ShowPlanXML>", 0, true)]
    [InlineData("<ShowPlanXML><RelOp PhysicalOp='Constant Scan'/></ShowPlanXML>", 1, false)]
    [InlineData("<ShowPlanXML><RelOp PhysicalOp='Index Seek'><Object Table='T'/></RelOp></ShowPlanXML>", 0, false)]
    [InlineData("<ShowPlanXML><RelOp PhysicalOp='Constant Scan'><Object Table='T'/></RelOp></ShowPlanXML>", 0, false)]
    [InlineData("<ShowPlanXML/>", 0, false)]
    [InlineData("<ShowPlanXML><RelOp PhysicalOp='Constant Scan'/><RelOp PhysicalOp='Index Scan'/></ShowPlanXML>", 0, false)]
    public void FolderCostIo_OnlyProvenEmptyConstantPlansAllowNoMessages(string plan, int rows, bool allowed) =>
        Assert.Equal(allowed, ClipboardPermissionQueries.IsZeroIoConstantPlan([plan], rows));

    [Fact]
    public async Task FolderCostOrder_SendsExactLowercasePositionThroughActualHttpHelper()
    {
        using var handler = new FolderCostWireCapture();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var feature = new FeaturePoint("wire-position", "Workspace", "moveUp", "action", "wire", null,
            "workspace.folder.reorder", [], 0);
        using var response = await FeatureHttpTestRequests.SendAsync(client, feature, HttpMethod.Post,
            "/api/workspace/content/tree-order", FolderCostOrderBody(new("space", "root", ["path-link"]), "selected-link"));
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal(new[] { "parentPosition", "linkId", "direction" }, root.EnumerateObject().Select(property => property.Name));
        var position = root.GetProperty("parentPosition");
        Assert.Equal(new[] { "spaceId", "rootFolderId", "linkIds" }, position.EnumerateObject().Select(property => property.Name));
        Assert.Equal("space", position.GetProperty("spaceId").GetString());
        Assert.Equal("root", position.GetProperty("rootFolderId").GetString());
        Assert.Equal("path-link", Assert.Single(position.GetProperty("linkIds").EnumerateArray()).GetString());
        Assert.Equal("selected-link", root.GetProperty("linkId").GetString());
        Assert.Equal("up", root.GetProperty("direction").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "定位空直接配置读取的实际SQL、XML与IO，不作为业务成本样本")]
    public async Task FolderCostEmptyConfiguration_RecordsExactReplayShape()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var scope = fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        using var queries = new ClipboardPermissionQueries(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        queries.Begin();
        var configuration = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Authorization.Services.DataScopeObjectGrants>()
            .GetAsync("workspace.folder", "empty-config-314-" + Guid.NewGuid().ToString("N"), default);
        queries.End();
        Assert.Empty(configuration.Subjects);
        var diagnostic = Path.Combine(ClipboardCostEvidenceRoot(), "314", "empty-configuration-" + Guid.NewGuid().ToString("N"));
        output.WriteLine("Exact replay diagnostic: " + diagnostic);
        await queries.ExplainAsync(connectionString, diagnostic);
    }

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Workspace不同读取点、空/有效父初始化、编辑和排序成本")]
    public async Task FolderExecution_RecordsReadInitializationAndOrderCosts()
    {
        var selection = Environment.GetEnvironmentVariable("NEWP_FOLDER_COST_SAMPLES") ?? "all";
        Assert.Contains(selection, new[] { "all", "remaining", "writes", "reorder" });
        var cost = new ClipboardCost();
        var clock = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(cost, clock, new FolderCopyTransactionClock(clock))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var parent = await Folder(fixture, admin, root, "Folder成本父容器");
        for (var index = 0; index < 3; index++) await Folder(fixture, admin, parent, "Folder成本兄弟" + index);
        var entry = Point(fixture, "workspace.spaces.access");
        var create = Point(fixture, "workspace.folder.create");
        var edit = RowPoint(fixture, "edit");
        var reorder = Assert.Single(fixture.FeatureCatalog, point => point.ActionScope == "contextMenu" && point.Key == "moveUp");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FolderExecutionCost", "Folder执行成本",
            "folder-execution-cost", "Folder执行成本主体", entry, create, edit, reorder);
        using var ordinary = actor.Client;
        using (var ensured = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure"))
            Assert.Equal(HttpStatusCode.OK, ensured.StatusCode);
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var parentId = FolderId(parent, links);
        var selected = links.Where(row => Equals(row["PEID"], parentId)).OrderBy(row => (long)row["TreeOrdinal"]!).Last();
        var selectedId = (string)selected["CEID"]!; var selectedLink = (string)selected["LID"]!;
        string connectionString; string departmentId;
        using (var scope = fixture.Services.CreateScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
            departmentId = (await scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.Ownership.IAccountOwnershipDirectory>()
                .FindManyAsync([actor.AccountId]))[actor.AccountId].DepartmentId!;
        }
        Assert.False(string.IsNullOrWhiteSpace(departmentId));
        using var timing = new ClipboardSqlTiming(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        using var permissions = new ClipboardPermissionQueries(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        var stage = Environment.GetEnvironmentVariable("NEWP_EXECUTION_COST_STAGE") ?? "candidate";
        Assert.Matches("^[a-zA-Z0-9-]+$", stage);
        var run = Path.Combine(ClipboardCostEvidenceRoot(), "314", "folder-cost-" + stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        await File.WriteAllTextAsync(Path.Combine(run, "candidate.json"), JsonSerializer.Serialize(new
        { stage, productionBaseline = "1d9191e0f3b004d564481abc85c290b7d4688838", plan.FeatureCatalog, plan.ActionDataAccessCatalog }));
        var nodeKey = Uri.EscapeDataString(LinkTreeNodeKey.Encode(new(parent.SpaceId, parent.RootFolderId, parent.LinkIds)));
        var tree = "/api/trees/workspace.structure/";
        await Measure("tree-roots", entry, "workspace.structure.read", HttpMethod.Get, tree + "roots?scope=currentUser");
        await Measure("tree-children", entry, "workspace.structure.read", HttpMethod.Get, tree + "children?scope=currentUser&nodeKey=" + nodeKey);
        await Measure("tree-path", entry, "workspace.structure.read", HttpMethod.Get, tree + "path?scope=currentUser&nodeKey=" + nodeKey);
        await Measure("tree-search", entry, "workspace.structure.read", HttpMethod.Get, tree + "search?scope=currentUser&keyword=" + Uri.EscapeDataString("Folder成本兄弟") + "&page=1&pageSize=2");
        var queryScope = Uri.EscapeDataString(JsonSerializer.Serialize(FolderCostListScope(parent)));
        await Measure("content-page", entry, "workspace.content.read", HttpMethod.Get,
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(parentId) + "&queryScope=" + queryScope + "&pageSize=2");
        await Measure("candidates", entry, "workspace.folder.candidates", HttpMethod.Get, "/api/workspace/folders/candidates?pageSize=2&keyword=" + Uri.EscapeDataString("Folder成本兄弟"));
        await Measure("detail", entry, "workspace.folder.read", HttpMethod.Get, "/api/workspace/folders/" + selectedId);
        await Measure("create-empty-parent", create, "workspace.folder.create", HttpMethod.Post, "/api/workspace/content/folders",
            new { position = parent, values = new { ENAME = "Folder成本空父创建" }, copyParentAuthorization = true });
        DataScopeDirectSubjectDto[] subjects = [
            new("CostCreator314", "creator", null, 10, new Dictionary<string, string> { ["read"] = "deny" }, ExpiresAt: "2099-01-01T00:00:00Z"),
            new("CostActor314", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" }),
            new("CostDepartment314", "department", departmentId, 7, new Dictionary<string, string> { ["delete"] = "allow" }, new(true), "2099-02-01T00:00:00Z") ];
        var authorize = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.authorize" && point.Key == "authorize");
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, authorize, HttpMethod.Put,
            "/api/workspace/content/" + parent.LinkIds[^1] + "/permissions?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = root.SpaceId, rootFolderId = root.RootFolderId, linkIds = root.LinkIds })),
            new DataScopeObjectConfiguration("workspace.folder", parentId, subjects)))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        await Measure("create-configured-parent", create, "workspace.folder.create", HttpMethod.Post, "/api/workspace/content/folders",
            new { position = parent, values = new { ENAME = "Folder成本有效父创建" }, copyParentAuthorization = true });
        await Measure("edit", edit, "workspace.folder.edit", HttpMethod.Put, "/api/workspace/content/" + selectedLink,
            new { position = parent, values = new { ENAME = "Folder成本兄弟已编辑" } });
        await Measure("reorder", reorder, "workspace.folder.reorder", HttpMethod.Post, "/api/workspace/content/tree-order",
            FolderCostOrderBody(parent, selectedLink));

        async Task Measure(string operation, FeaturePoint feature, string execution, HttpMethod method, string path, object? body = null)
        {
            if (selection == "remaining" && operation.StartsWith("tree-", StringComparison.Ordinal)) return;
            if (selection is "writes" or "reorder" && operation is not ("create-empty-parent" or "create-configured-parent" or "edit" or "reorder")) return;
            // candidate选择该样本真实view/surface，纯API候选明确不借用树或列表声明。
            ActionDataAccessRequirement? declaration = null;
            if (stage != "baseline")
            {
                if (operation == "candidates")
                {
                    var api = Assert.Single(plan.Modules.SelectMany(module => module.DataExecutions), item => item.Key == WorkspaceFolderExecutions.Candidates);
                    Assert.Equal("api", api.Boundary);
                    Assert.Contains(api.FixedBindings, source => source.Kind == "api" && source.Identity == WorkspaceFolderExecutions.Candidates
                        && source.HttpMethod == "GET" && source.Route == "/api/workspace/folders/candidates");
                }
                else
                {
                    var view = operation.StartsWith("tree-", StringComparison.Ordinal) ? "workspace.structure"
                        : operation == "detail" ? "workspace.folder.detail" : "workspace.folder.list";
                    var surface = operation.StartsWith("create-", StringComparison.Ordinal) ? "pageActions"
                        : operation == "edit" ? "rowActions" : operation == "reorder" ? "featureActions" : "read";
                    var action = surface == "pageActions" ? "create" : surface == "rowActions" ? "edit" : surface == "featureActions" ? "moveUp" : "";
                    declaration = Assert.Single(plan.ActionDataAccessCatalog, item => (item.Execution == execution || item.BusinessBinding == feature.BehaviorKey) && item.ViewKey == view
                        && item.Surface == surface && item.ActionKey == action && item.FeaturePositions.Contains(feature.Position));
                }
            }
            // 单独恢复排序时仍准备相同两次创建及编辑后的状态；准备请求不计入成本样本。
            if (selection == "reorder" && operation != "reorder") { await Execute(); return; }
            cost.Begin(); timing.Begin(); clock.Begin(); permissions.Begin();
            var watch = Stopwatch.StartNew(); object database; object sqlTiming;
            try { await Execute(); }
            finally { watch.Stop(); database = cost.End(); sqlTiming = timing.End(); permissions.End(); }
            // 先保留已完成请求的原始计数，回放采集失败时也不会丢失实际到达阶段。
            await File.WriteAllTextAsync(Path.Combine(run, operation + "-request.json"), JsonSerializer.Serialize(new
            { stage, operation, database, sqlTiming, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                graphLockWaitMs = clock.WaitMs, graphLockHoldMs = clock.HoldMs, transactionMs = clock.TransactionMs },
                new JsonSerializerOptions { WriteIndented = true }));
            var permissionPlans = await permissions.ExplainAsync(connectionString, Path.Combine(run, operation + "-replay"));
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
            { stage, operation, ordinaryRoles = 1, parentDirectSubjects = operation is "create-configured-parent" or "edit" or "reorder" ? 3 : 0,
                initialSiblings = 3, pageSize = 2, pooling = false, database, sqlTiming,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, graphLockWaitMs = clock.WaitMs,
                graphLockHoldMs = clock.HoldMs, transactionMs = clock.TransactionMs, permissionPlans }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: {JsonSerializer.Serialize(sqlTiming)}, artifacts={run}");

            async Task Execute()
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, method, path, body, declaration);
                Assert.True(response.StatusCode == HttpStatusCode.OK, operation + ": HTTP " + (int)response.StatusCode);
                await response.Content.ReadAsByteArrayAsync();
            }
        }
    }
    [Fact]
    public void FolderCostScope_RoundTripsExistingStringTransportAndPositionParser()
    {
        var position = new WorkspacePosition("scope-space", "scope-root", ["scope-link"]);
        var wire = Uri.EscapeDataString(JsonSerializer.Serialize(FolderCostListScope(position)));
        var scope = JsonSerializer.Deserialize<Dictionary<string, string?>>(Uri.UnescapeDataString(wire))!;
        var actual = WorkspacePositions.FromScope(scope);
        Assert.Equal(position.SpaceId, actual.SpaceId);
        Assert.Equal(position.RootFolderId, actual.RootFolderId);
        Assert.Equal(position.LinkIds, actual.LinkIds);
        // 命令/permissions仍接受数组；该形状不能作为标准列表string字典传输。
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<string, string?>>(
            JsonSerializer.Serialize(new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds })));
    }

    private static Dictionary<string, string?> FolderCostListScope(WorkspacePosition position) => new(StringComparer.Ordinal)
    {
        ["spaceId"] = position.SpaceId, ["rootFolderId"] = position.RootFolderId,
        ["linkIds"] = JsonSerializer.Serialize(position.LinkIds)
    };

    private static object FolderCostOrderBody(WorkspacePosition position, string linkId) => new
    {
        parentPosition = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds },
        linkId, direction = "up"
    };

    private sealed class FolderCostWireCapture : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK);
        }
    }
}
