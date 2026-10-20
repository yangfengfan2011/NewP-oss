using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通有限功能账号的深链128及高共享DAG成功成本，与管理员样本分开记录")]
    public async Task StructureCopy_MeasuresOrdinaryActorSuccessfulDeepAndSharedGraphs()
    {
        var minimumSlice = Environment.GetEnvironmentVariable("NEWP_WORKSPACE_FOLDER_COST_SLICE") == "320";
        foreach (var shape in minimumSlice ? new[] { "dag" } : new[] { "deep", "dag" })
        {
            var size = shape == "deep" ? 128 : 16;
            var statistics = new FolderCopyStatistics(); var graph = new FolderCopyLockClock();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(statistics, graph, new FolderCopyTransactionClock(graph))), includeWorkspace: true);
            using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "普通账号容量来源");
            var dimensions = await SeedStructureCapacityAsync(fixture, source, shape, size);
            var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StructureOrdinaryCost", "结构容量账号", "structure-ordinary-cost", "结构容量账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure"));
            using var ordinary = actor.Client;
            using var permissionScope = fixture.Services.CreateScope();
            var permissionConnection = permissionScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
            using var permissions = minimumSlice ? new ClipboardPermissionQueries(new SqlConnectionStringBuilder(permissionConnection).InitialCatalog) : null;
            var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "普通账号独立结构" };
            if (shape == "dag")
            {
                permissions?.Begin();
                object preparation;
                try { preparation = await MeasureFolderCopyAsync(statistics, graph, async () => { await Command(fixture, ordinary, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure/prepare", new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition }); }); }
                finally { permissions?.End(); }
                if (permissions is not null)
                {
                    var evidence = Path.Combine(Environment.GetEnvironmentVariable("NEWP_COPY_COST_ARTIFACT_ROOT")!, "structure-prepare");
                    var plans = await permissions.ExplainAsync(permissionConnection, evidence);
                    await File.WriteAllTextAsync(Path.Combine(evidence, "measurement.json"), JsonSerializer.Serialize(new { shape, size, dimensions, preparation, plans }));
                }
                output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new { scenario = "prepare-only", actorKind = "ordinary-limited-features", rules = "formal default object rules; no synthetic complex ACL", sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth, result = preparation }));
            }
            var foldersBefore = await Rows(fixture, "WorkspaceFolder"); var linksBefore = await Rows(fixture, "WorkspaceFolderContent");
            permissions?.Begin();
            object result;
            try { result = await MeasureFolderCopyAsync(statistics, graph, async () => { await Command(fixture, ordinary, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", input); }); }
            finally { permissions?.End(); }
            if (permissions is not null)
            {
                var evidence = Path.Combine(Environment.GetEnvironmentVariable("NEWP_COPY_COST_ARTIFACT_ROOT")!, "structure-submit");
                var plans = await permissions.ExplainAsync(permissionConnection, evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, "measurement.json"), JsonSerializer.Serialize(new { shape, size, dimensions, result, plans }));
            }
            output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new {
                environment = new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount, os = Environment.OSVersion.ToString(), sql = "repository configured SQL Server; independent test catalog; Release" },
                actorKind = "ordinary-limited-features", features = new[] { "workspace.spaces.access", "workspace.folder.pasteFolderStructure" }, rules = "formal default object rules; no synthetic complex ACL",
                shape, size, sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth,
                storedFoldersBefore = foldersBefore.Length, storedLinksBefore = linksBefore.Length, result,
                acceptance = "Measured ordinary-user success only; no SLA or capacity approval." }));
            var created = (await Rows(fixture, "WorkspaceFolder")).Where(row => !foldersBefore.Any(old => Equals(old["EID"], row["EID"]))).ToArray();
            Assert.Equal(dimensions.Nodes, created.Length); Assert.All(created, row => Assert.Equal(actor.AccountId, row["CreatedBy"]));
            Assert.Equal(linksBefore.Length + dimensions.Edges + 1, (await Rows(fixture, "WorkspaceFolderContent")).Length);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "宽分支、深链及高共享DAG递增的真实端点成本，独立样本不枚举出现路径")]
    public async Task StructureCopy_MeasuresIncreasingWideDeepAndSharedGraphs()
    {
        foreach (var shape in new[] { "wide", "deep", "dag" })
        foreach (var size in shape == "dag" ? new[] { 4, 8, 16 } : new[] { 8, 32, 128 })
        {
            var statistics = new FolderCopyStatistics(); var graph = new FolderCopyLockClock();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(statistics, graph, new FolderCopyTransactionClock(graph))), includeWorkspace: true);
            using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "容量来源");
            var dimensions = await SeedStructureCapacityAsync(fixture, source, shape, size);
            var folders = await Rows(fixture, "WorkspaceFolder"); var links = await Rows(fixture, "WorkspaceFolderContent");
            var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "容量独立结构" };
            if (shape == "dag" && size == 16)
            {
                var preparation = await MeasureFolderCopyAsync(statistics, graph, async () => { await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure/prepare", new { input.mode, input.sourcePosition, input.linkIds, input.targetPosition }); });
                output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new { scenario = "prepare-only", sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth, result = preparation, scope = "Read-only source and target preparation; excludes user typing and submit." }));
            }
            var result = await MeasureFolderCopyAsync(statistics, graph, async () => { await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", input); });
            output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new {
                environment = new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount, os = Environment.OSVersion.ToString(), sql = "repository configured SQL Server; independent test catalog; Release" },
                shape, size, sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth,
                distinctSourceLeaves = shape == "wide" ? size : shape == "dag" ? 4 : 1,
                conceptualLeafPaths = shape == "dag" ? Math.Pow(4, size) : shape == "wide" ? size : 1,
                storedFoldersBefore = folders.Length, storedLinksBefore = links.Length, result,
                acceptance = "Measured evidence only; no approved SLA or business capacity limit." }));
            Assert.Equal(folders.Length + dimensions.Nodes, (await Rows(fixture, "WorkspaceFolder")).Length);
            Assert.Equal(links.Length + dimensions.Edges + 1, (await Rows(fixture, "WorkspaceFolderContent")).Length);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "深层拒绝、图锁等待、SQL写后取消的真实读取/内存/事务/回滚成本")]
    public async Task StructureCopy_MeasuresDeepRejectionLockWaitingAndCancellation()
    {
        var statistics = new FolderCopyStatistics(); var graph = new FolderCopyLockClock(); var control = new PasteCommitControl();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(statistics, graph, new FolderCopyTransactionClock(graph), control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "等待来源");
        var dimensions = await SeedStructureCapacityAsync(fixture, source, "deep", 32);
        var leaf = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "node-32"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StructureCost", "结构测量", "structure-cost", "结构测量", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderStructure"));
        using var ordinary = actor.Client; await Rule(fixture, admin, "DeepCostDenied", "workspace.folder", (string)leaf["EID"]!, "workspaceCopy", "deny");
        object Input(string name) => new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name };
        var rejected = await MeasureFolderCopyAsync(statistics, graph, async () => { using var response = await Send(fixture, ordinary, "workspace.folder.pasteFolderStructure", "folders/copy-structure", Input("拒绝")); await Problem(response, HttpStatusCode.Forbidden, "Workspace.FolderCopyUnavailable"); });
        output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new { scenario = "deep-copy-denial", sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth, result = rejected }));
        string connectionString; using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().ConnectionString;
        await using (var holder = new SqlConnection(connectionString))
        {
            await holder.OpenAsync(); await using var transaction = await holder.BeginTransactionAsync();
            await using var command = holder.CreateCommand(); command.Transaction = (SqlTransaction)transaction;
            command.CommandText = "EXEC sp_getapplock @Resource=N'Workspace.FolderGraph', @LockMode='Exclusive', @LockOwner='Transaction';"; await command.ExecuteNonQueryAsync();
            var waiting = MeasureFolderCopyAsync(statistics, graph, async () => { await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", Input("等待成功")); });
            await graph.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(20)); await Task.Delay(250); await transaction.CommitAsync();
            output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new { scenario = "concurrent-graph-lock-wait", sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth, controlledHolderDelayMs = 250, result = await waiting }));
        }
        var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var changes = Snapshot(await Rows(fixture, "BusinessChange"));
        control.Pause = true; double rollbackMs = 0;
        var cancelled = await MeasureFolderCopyAsync(statistics, graph, async () => {
            using var cancellation = new CancellationTokenSource(); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy-structure") { Content = JsonContent.Create(Input("取消")) };
            request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderStructure").Position);
            var pending = admin.SendAsync(request, cancellation.Token);
            try
            {
                await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30)); Assert.True(control.WrittenReferences >= dimensions.Edges + 1); Assert.True(control.WrittenChanges >= dimensions.Edges + 1);
                var clock = Stopwatch.StartNew(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20)); rollbackMs = clock.Elapsed.TotalMilliseconds;
            }
            finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        });
        output.WriteLine("STRUCTURE_CAPACITY " + JsonSerializer.Serialize(new { scenario = "cancel-after-sql-before-commit", sourceNodes = dimensions.Nodes, sourceEdges = dimensions.Edges, depth = dimensions.Depth, rollbackMs, result = cancelled }));
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    private static async Task<(int Nodes, int Edges, int Depth)> SeedStructureCapacityAsync(BusinessAuditHostFixture fixture, WorkspacePosition source, string shape, int size)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); var ids = scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.ObjectIds.IPlatformObjectIdGenerator>();
        var sourceLinkId = source.LinkIds[^1];
        var incoming = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == sourceLinkId);
        var sourceId = (string)incoming["CEID"]!; var seed = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == sourceId);
        var count = shape == "dag" ? 4 * size : size; var nodeIds = new string[count + 1]; nodeIds[0] = sourceId;
        for (var index = 1; index <= count; index++) { nodeIds[index] = ids.NewId("WorkspaceFolder"); db.Set<Dictionary<string, object?>>("WorkspaceFolder").Add(new(seed) { ["EID"] = nodeIds[index], ["ENAME"] = "node-" + index, ["Description"] = "容量说明" }); }
        var edges = new List<(int Parent, int Child, long Ordinal)>();
        if (shape == "wide") for (var index = 1; index <= count; index++) edges.Add((0, index, index));
        else if (shape == "deep") for (var index = 1; index <= count; index++) edges.Add((index - 1, index, 1));
        else for (var level = 0; level < size; level++) for (var parent = 0; parent < (level == 0 ? 1 : 4); parent++) for (var child = 0; child < 4; child++)
            edges.Add((level == 0 ? 0 : 1 + (level - 1) * 4 + parent, 1 + level * 4 + child, child + 1));
        foreach (var edge in edges) db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Add(new(incoming) { ["LID"] = ids.NewId("WorkspaceFolderContent"), ["PEID"] = nodeIds[edge.Parent], ["CEID"] = nodeIds[edge.Child], ["TreeOrdinal"] = edge.Ordinal });
        await db.SaveChangesAsync(); return (count + 1, edges.Count, shape == "wide" ? 1 : size);
    }
}
