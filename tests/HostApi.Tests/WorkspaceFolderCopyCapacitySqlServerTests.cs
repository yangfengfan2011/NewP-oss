using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实混合直接内容递增测量，不把实测成本作为批准容量预算")]
    public async Task ShallowCopy_MeasuresIncreasingMixedContentsAndPreservesDocumentFiles()
    {
        var statistics = new FolderCopyStatistics(); var lockClock = new FolderCopyLockClock(); var transactionClock = new FolderCopyTransactionClock(lockClock);
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(statistics, lockClock, transactionClock)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "容量来源"); await Folder(fixture, admin, source, "共享子目录");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SHALLOW-SCALE-0000", ENAME = "容量零组件" } });
        var values = new { ECODE = "SHALLOW-SCALE-DOC", ENAME = "共享文件图文档" };
        using (var multipart = new MultipartFormDataContent())
        {
            multipart.Add(new StringContent(JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "position"); multipart.Add(new StringContent(JsonSerializer.Serialize(values)), "values");
            var file = new ByteArrayContent([1, 2, 3]); file.Headers.ContentType = new("application/pdf"); multipart.Add(file, "file", "shallow.pdf");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = multipart };
            var createDocument = Point(fixture, "workspace.folder.createDocument");
            request.Headers.Add("X-NewP-Feature", createDocument.Position);
            request.Headers.Add("X-NewP-Data-Declaration", new FeatureHttpTestRequests(fixture)
                .Declaration("workspace.content.createDocument", createDocument).Id);
            using var response = await admin.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var uploadId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uploadId").GetString();
            await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = uploadId, attachmentUploadIds = Array.Empty<string>() } });
        }
        var fileModels = new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "FileUploadLease", "FileRecord" };
        var fileFacts = new Dictionary<string, string>(); foreach (var model in fileModels) fileFacts[model] = Snapshot(await Rows(fixture, model));
        var files = Directory.GetFiles(fixture.StorageRoot).ToDictionary(path => path, File.ReadAllBytes);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowOrdinaryCost", "浅副本容量账号", "shallow-ordinary-cost", "浅副本容量账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderCopy"));
        using var ordinary = actor.Client;
        var sourceId = FolderId(source, await Rows(fixture, "WorkspaceFolderContent")); var previous = 3;
        var minimal = Environment.GetEnvironmentVariable("NEWP_WORKSPACE_FOLDER_COST_SLICE") == "320";
        using var permissionScope = fixture.Services.CreateScope();
        var permissionConnection = permissionScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        using var permissions = minimal ? new ClipboardPermissionQueries(new SqlConnectionStringBuilder(permissionConnection).InitialCatalog) : null;
        foreach (var size in Environment.GetEnvironmentVariable("NEWP_WORKSPACE_FOLDER_COST_SLICE") == "320" ? new[] { 3 } : new[] { 3, 30, 100, 300 })
        {
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); var ids = scope.ServiceProvider.GetRequiredService<ICP.Platform.Crud.ObjectIds.IPlatformObjectIdGenerator>();
                var seed = await db.Set<Dictionary<string, object?>>("Part").AsNoTracking().FirstAsync();
                var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().FirstAsync(row => EF.Property<string>(row, "PEID") == sourceId && EF.Property<string>(row, "ChildModelKey") == "Part");
                for (var index = previous; index < size; index++)
                {
                    var id = ids.NewId("Part"); db.Set<Dictionary<string, object?>>("Part").Add(new(seed) { ["EID"] = id, ["ECODE"] = "SHALLOW-SCALE-" + index.ToString("D4") });
                    db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Add(new(link) { ["LID"] = ids.NewId("WorkspaceFolderContent"), ["CEID"] = id });
                }
                await db.SaveChangesAsync();
            }
            previous = size;
            permissions?.Begin();
            object result;
            try { result = await MeasureFolderCopyAsync(statistics, lockClock, async () => {
                await Command(fixture, ordinary, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "容量副本" + size }); }); }
            finally { permissions?.End(); }
            if (permissions is not null)
            {
                var evidence = Path.Combine(Environment.GetEnvironmentVariable("NEWP_COPY_COST_ARTIFACT_ROOT")!, "shallow");
                var plans = await permissions.ExplainAsync(permissionConnection, evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, "measurement.json"), JsonSerializer.Serialize(new { directNodes = size + 1, directEdges = size, result, plans }));
            }
            output.WriteLine("SHALLOW_CAPACITY " + JsonSerializer.Serialize(new { environment = new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount, os = Environment.OSVersion.ToString(), sql = "local repository configured SQL Server; test catalog; Release" }, actorKind = "ordinary-limited-features", rules = "formal default object rules; no synthetic complex ACL", directNodes = size + 1, directEdges = size, depth = 1, folder = 1, document = 1, part = size - 2, result, acceptance = "Measured evidence only; no SLA or capacity acceptance and no business hard limit." }));
            var rows = await Rows(fixture, "WorkspaceFolder"); var copyId = (string)Assert.Single(rows, row => Equals(row["ENAME"], "容量副本" + size))["EID"]!;
            Assert.Equal(size, (await Rows(fixture, "WorkspaceFolderContent")).Count(row => Equals(row["PEID"], copyId)));
        }
        foreach (var model in fileModels) Assert.Equal(fileFacts[model], Snapshot(await Rows(fixture, model)));
        foreach (var (path, bytes) in files) Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(files.Count, Directory.GetFiles(fixture.StorageRoot).Length);
    }

    private static async Task<object> MeasureFolderCopyAsync(FolderCopyStatistics statistics, FolderCopyLockClock lockClock, Func<Task> action)
    {
        using var process = Process.GetCurrentProcess(); process.Refresh(); var baseline = process.WorkingSet64; long peak = baseline;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () => { while (!stop.IsCancellationRequested) { process.Refresh(); Interlocked.Exchange(ref peak, Math.Max(Interlocked.Read(ref peak), process.WorkingSet64)); try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { } } });
        statistics.Begin(); lockClock.Begin(); var clock = Stopwatch.StartNew();
        try { await action(); }
        finally { clock.Stop(); stop.Cancel(); await sampler; }
        return new { totalMs = clock.Elapsed.TotalMilliseconds, processWorkingSetBaselineBytes = baseline, sampledPeakWorkingSetBytes = peak, sampledIncreaseBytes = peak - baseline,
            graphLockWaitMs = lockClock.WaitMs, lockHoldMs = lockClock.HoldMs, transactionMs = lockClock.TransactionMs, sqlClient = statistics.End() };
    }

    private sealed class FolderCopyStatistics : DbCommandInterceptor
    {
        private bool active; private readonly HashSet<SqlConnection> connections = [];
        internal void Begin() { connections.Clear(); active = true; }
        internal object End() { active = false; return connections.Select(connection => connection.RetrieveStatistics().Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value)).ToArray(); }
        private void Observe(DbCommand command) { if (active && command.Connection is SqlConnection connection && connections.Add(connection)) { connection.StatisticsEnabled = true; connection.ResetStatistics(); } }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken token = default) { Observe(command); return ValueTask.FromResult(result); }
    }
    private sealed class FolderCopyLockClock : DbCommandInterceptor
    {
        internal long Acquired; internal long Started; internal long LockStarted; internal double HoldMs; internal double TransactionMs; internal double WaitMs;
        internal TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Begin() { Acquired = Started = LockStarted = 0; HoldMs = TransactionMs = WaitMs = 0; Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        { if (command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal) && LockStarted == 0) { LockStarted = Stopwatch.GetTimestamp(); Waiting.TrySetResult(); } return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken token = default)
        { if (command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal) && Acquired == 0) { Acquired = Stopwatch.GetTimestamp(); WaitMs = Stopwatch.GetElapsedTime(LockStarted, Acquired).TotalMilliseconds; } return ValueTask.FromResult(result); }
    }
    private sealed class FolderCopyTransactionClock(FolderCopyLockClock clock) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken token = default) { if (clock.Started == 0) clock.Started = Stopwatch.GetTimestamp(); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default) { End(); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default) { End(); return Task.CompletedTask; }
        private void End() { if (clock.Acquired != 0) clock.HoldMs = Stopwatch.GetElapsedTime(clock.Acquired).TotalMilliseconds; if (clock.Started != 0) clock.TransactionMs = Stopwatch.GetElapsedTime(clock.Started).TotalMilliseconds; }
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本拒绝、真实图锁并发等待及写入后取消的内存/往返/事务/回滚成本")]
    public async Task ShallowCopy_MeasuresRejectionGraphLockWaitingAndCancellation()
    {
        var statistics = new FolderCopyStatistics(); var lockClock = new FolderCopyLockClock(); var control = new PasteCommitControl();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(statistics, lockClock, new FolderCopyTransactionClock(lockClock), control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "拒绝等待来源");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SHALLOW-CANCEL", ENAME = "直接内容" } });
        var part = Assert.Single(await Rows(fixture, "Part"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ShallowCost", "容量账号", "shallow-cost", "容量账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.pasteFolderCopy"));
        using var ordinary = actor.Client;
        await Rule(fixture, admin, "CostDenied", "part.part", (string)part["EID"]!, "partCopy", "deny");
        object Input(string name) => new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name };
        var rejected = await MeasureFolderCopyAsync(statistics, lockClock, async () => { using var response = await Send(fixture, ordinary, "workspace.folder.pasteFolderCopy", "folders/copy", Input("拒绝")); await Problem(response, HttpStatusCode.Forbidden, "Workspace.FolderCopyUnavailable"); });
        output.WriteLine("SHALLOW_CAPACITY " + JsonSerializer.Serialize(new { scenario = "direct-copy-denial", directNodes = 2, directEdges = 1, depth = 1, rejected }));
        string connectionString; using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().ConnectionString;
        await using (var holder = new SqlConnection(connectionString))
        {
            await holder.OpenAsync(); await using var transaction = await holder.BeginTransactionAsync();
            await using var command = holder.CreateCommand(); command.Transaction = (SqlTransaction)transaction;
            command.CommandText = "EXEC sp_getapplock @Resource=N'Workspace.FolderGraph', @LockMode='Exclusive', @LockOwner='Transaction';"; await command.ExecuteNonQueryAsync();
            var waiting = MeasureFolderCopyAsync(statistics, lockClock, async () => { await Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", Input("等待成功")); });
            await lockClock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(20)); await Task.Delay(250); await transaction.CommitAsync();
            output.WriteLine("SHALLOW_CAPACITY " + JsonSerializer.Serialize(new { scenario = "concurrent-graph-lock-wait", directNodes = 2, directEdges = 1, depth = 1, controlledHolderDelayMs = 250, result = await waiting }));
        }
        var folders = Snapshot(await Rows(fixture, "WorkspaceFolder")); var links = Snapshot(await Rows(fixture, "WorkspaceFolderContent")); var changes = Snapshot(await Rows(fixture, "BusinessChange"));
        control.Pause = true; double rollbackMs = 0;
        var cancelled = await MeasureFolderCopyAsync(statistics, lockClock, async () => {
            using var cancellation = new CancellationTokenSource(); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders/copy") { Content = JsonContent.Create(Input("取消")) };
            request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.pasteFolderCopy").Position);
            var pending = admin.SendAsync(request, cancellation.Token);
            try { await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); var clock = Stopwatch.StartNew(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20)); rollbackMs = clock.Elapsed.TotalMilliseconds; }
            finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await pending).Dispose(); } catch (OperationCanceledException) { } }
        });
        output.WriteLine("SHALLOW_CAPACITY " + JsonSerializer.Serialize(new { scenario = "cancel-after-sql-before-commit", directNodes = 2, directEdges = 1, depth = 1, rollbackMs, cancelled }));
        Assert.Equal(folders, Snapshot(await Rows(fixture, "WorkspaceFolder"))); Assert.Equal(links, Snapshot(await Rows(fixture, "WorkspaceFolderContent"))); Assert.Equal(changes, Snapshot(await Rows(fixture, "BusinessChange")));
    }
}

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本等待真实引用移除事务后重取完整源图，不提交锁前的旧计划")]
    public async Task ShallowCopy_ReplansAfterConcurrentSourceGraphChange()
    {
        var control = new PasteCommitControl(); var graph = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control, graph)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "并发图来源");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts", new { position = source, values = new { ECODE = "SHALLOW-GRAPH", ENAME = "保留实体" } });
        var links = await Rows(fixture, "WorkspaceFolderContent"); var sourceId = FolderId(source, links); var direct = Assert.Single(links, row => Equals(row["PEID"], sourceId)); var part = Snapshot(await Rows(fixture, "Part"));
        control.Pause = true;
        var removing = Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = source, linkIds = new[] { (string)direct["LID"]! } });
        Task<JsonElement>? copying = null;
        try
        {
            await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); graph.Begin();
            copying = Command(fixture, admin, "workspace.folder.pasteFolderCopy", "/api/workspace/content/folders/copy", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "重取图副本" });
            await graph.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(20)); control.Pause = false; control.Resume.TrySetResult();
            await removing; await copying;
        }
        finally { control.Pause = false; control.Resume.TrySetResult(); await removing; if (copying is not null) await copying; }
        var clone = Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "重取图副本"));
        var after = await Rows(fixture, "WorkspaceFolderContent"); Assert.DoesNotContain(after, row => Equals(row["PEID"], sourceId) || Equals(row["PEID"], clone["EID"]));
        Assert.Equal(part, Snapshot(await Rows(fixture, "Part")));
    }
}
