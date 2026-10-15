using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceSearchSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实24层多父图完整计数16777216条出现，仅在数据库构造当前页并记录成本及实际计划")]
    public async Task Search_CountsConvergingDagWithoutMaterializingEveryPath_AndPropagatesCancellationAndTimeout()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var seed = await Create(admin, fixture, root, "汇聚入口"); var seedId = await FolderId(admin, entry, seed);
        await SeedDag(fixture, seedId, 24, "cost");
        string database; using (var scope = fixture.Services.CreateScope()) database = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetDbConnection().Database;
        using var probe = new SearchSqlObservation(database);
        foreach (var page in new[] { 1, 838861 })
        {
            var allocated = GC.GetTotalAllocatedBytes(); probe.Begin();
            var result = await Search(admin, entry, "末层命中", page, 20); var cost = probe.End();
            Assert.Equal(16777216, result.GetProperty("total").GetInt64());
            Assert.Equal(page == 1 ? 20 : 16, result.GetProperty("items").GetArrayLength());
            Assert.All(result.GetProperty("items").EnumerateArray(), item => Assert.Equal(25, item.GetProperty("node").GetProperty("context").GetProperty("linkIds").GetArrayLength()));
            output.WriteLine(JsonSerializer.Serialize(new { scenario = "24-level two-node DAG complete count and page", page, nodes = 50, edges = 95, representedMatches = 16777216, returnedItems = result.GetProperty("items").GetArrayLength(), responseBytes = System.Text.Encoding.UTF8.GetByteCount(result.GetRawText()), allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, cost }));
        }
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "actual execution plan for complete count and last page", plan = await probe.ReplayPlanAsync(fixture) }));
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "actual parameterized search command without parameter values", sql = probe.CapturedSql }));
        probe.Begin();
        await FeatureHttpTestRequests.ReadAsync(admin, entry, TreePathUrl(seed));
        var exactCost = probe.End(); Assert.Equal(0, probe.SearchCommands);
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "exact path reads do not execute occurrence search", exactCost }));

        // 同一读事务计数后暂停；并发改名必须等到本页及 total 一同读完，下一请求才观察新集合。
        probe.Delay = "00:00:02"; probe.DelayAfterCount = true; probe.Begin();
        var snapshot = Search(admin, entry, "末层命中", 1, 20);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await probe.WaitForDatabaseDelayAsync(fixture);
        using (var mutationScope = fixture.Services.CreateScope())
        {
            var mutationDb = mutationScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var update = mutationDb.Database.ExecuteSqlRawAsync("UPDATE WorkspaceFolder SET ENAME=N'并发改名' WHERE EID=N'cost-23-0'");
            await Task.Delay(50); Assert.False(update.IsCompleted);
            var old = await snapshot; Assert.Equal(16777216, old.GetProperty("total").GetInt64());
            Assert.All(old.GetProperty("items").EnumerateArray(), item => Assert.StartsWith("末层命中", item.GetProperty("node").GetProperty("label").GetString()));
            await update; probe.End(); probe.Delay = null; probe.DelayAfterCount = false;
            Assert.Equal(8388608, (await Search(admin, entry, "末层命中", 1, 1)).GetProperty("total").GetInt64());
            await mutationDb.Database.ExecuteSqlRawAsync("UPDATE WorkspaceFolder SET ENAME=N'末层命中0' WHERE EID=N'cost-23-0'");
        }
        // 控制真实数据库命令的等待，仅用于证明取消及 provider timeout 的传播，不添加生产测试入口。
        probe.Delay = "00:00:10"; probe.Begin(); using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/trees/workspace.structure/search?scope=currentUser&keyword=" + Uri.EscapeDataString("末层命中"));
        request.Headers.Add("X-NewP-Feature", entry.Position);
        var pending = admin.SendAsync(request, cancellation.Token);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await probe.WaitForDatabaseDelayAsync(fixture); var cancelClock = Stopwatch.StartNew(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); probe.End();
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "cancel actual SQL command", cancelElapsedMs = cancelClock.Elapsed.TotalMilliseconds, delaySeconds = 10 }));
        probe.Delay = "00:00:03"; probe.Timeout = 1; probe.Begin();
        using (var timeout = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, "/api/trees/workspace.structure/search?scope=currentUser&keyword=末层命中"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, timeout.StatusCode);
            Assert.Equal("Platform.TreeSearchIncomplete", (await timeout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "provider timeout incomplete", cost = probe.End() }));
        probe.Delay = null; probe.Timeout = null;
        Assert.Equal(16777216, (await Search(admin, entry, "末层命中", 1, 1)).GetProperty("total").GetInt64());
        await SeedDag(fixture, seedId, 54, "overflow");
        using var overflow = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, "/api/trees/workspace.structure/search?scope=currentUser&keyword=末层命中");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);
        Assert.Equal("Platform.TreeSearchIncomplete", (await overflow.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static async Task SeedDag(BusinessAuditHostFixture fixture, string start, int levels, string prefix)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var folders = db.Set<Dictionary<string, object?>>("WorkspaceFolder"); var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
        var folder = await folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == start);
        var startOrdinal = await links.Where(row => EF.Property<string>(row, "PEID") == start).MaxAsync(row => EF.Property<long?>(row, "TreeOrdinal")) ?? 0;
        var template = await links.AsNoTracking().FirstAsync(row => EF.Property<string>(row, "ChildModelKey") == "WorkspaceFolder");
        for (var level = 0; level < levels; level++)
            for (var side = 0; side < 2; side++)
            {
                var id = prefix + "-" + level + "-" + side;
                folders.Add(new(folder) { ["EID"] = id, ["ENAME"] = level == levels - 1 ? "末层命中" + side : "汇聚" + level + "-" + side });
                foreach (var parent in level == 0 ? new[] { start } : new[] { prefix + "-" + (level - 1) + "-0", prefix + "-" + (level - 1) + "-1" })
                    links.Add(new(template) { ["LID"] = Guid.NewGuid().ToString("N"), ["PEID"] = parent, ["CEID"] = id, ["TreeOrdinal"] = (long)(side + 1) + (level == 0 ? startOrdinal : 0) });
            }
        await db.SaveChangesAsync();
    }

    private sealed class SearchSqlObservation : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all; private readonly List<IDisposable> subscriptions = [];
        private readonly ConcurrentQueue<string> commands = new(); private readonly ConcurrentQueue<string> statistics = new();
        private readonly Stopwatch clock = new(); private readonly string database; private bool active;
        private string? sql; private SqlParameter[] parameters = []; private int sessionId;
        public string? Delay { get; set; } public bool DelayAfterCount { get; set; } public int? Timeout { get; set; }
        public string? CapturedSql => sql;
        public int SearchCommands => commands.Count(text => text == "Search");
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SearchSqlObservation(string database) { this.database = database; all = DiagnosticListener.AllListeners.Subscribe(this); }
        public void Begin() { commands.Clear(); statistics.Clear(); Started = new(TaskCreationOptions.RunContinuationsAsynchronously); clock.Restart(); active = true; }
        public object End() { active = false; clock.Stop(); return new { databaseCommands = commands.Count, searchCommands = SearchCommands, elapsedMs = clock.Elapsed.TotalMilliseconds, sqlStatistics = statistics.ToArray() }; }
        public void OnNext(DiagnosticListener listener) { if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!active || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command || command.Connection?.Database != database) return;
            var search = command.CommandText.Contains("/* Platform Link occurrence search */", StringComparison.Ordinal);
            if (value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal)) commands.Enqueue(search ? "Search" : "Other");
            if (!search || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)) return;
            sql = command.CommandText; parameters = command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray();
            if (command.Connection is SqlConnection connection) { sessionId = connection.ServerProcessId; connection.InfoMessage += (_, args) => { if (active) statistics.Enqueue(args.Message); }; }
            var delay = Delay is null ? "" : "WAITFOR DELAY '" + Delay + "'; ";
            var measured = DelayAfterCount ? command.CommandText.Replace("CREATE TABLE #LinkTreeSearchResults", delay + "CREATE TABLE #LinkTreeSearchResults", StringComparison.Ordinal) : delay + command.CommandText;
            command.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON; " + measured + " SET STATISTICS IO OFF; SET STATISTICS TIME OFF;";
            if (Timeout is { } timeout) command.CommandTimeout = timeout;
            Started.TrySetResult();
        }
        public async Task WaitForDatabaseDelayAsync(BusinessAuditHostFixture fixture)
        {
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await using var connection = new SqlConnection(db.Database.GetDbConnection().ConnectionString); await connection.OpenAsync();
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(5))
            {
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id=@session AND wait_type='WAITFOR'"; command.Parameters.AddWithValue("@session", sessionId);
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 1) return;
                await Task.Delay(20);
            }
            Assert.Fail("Controlled search command did not enter its actual database WAITFOR.");
        }
        public async Task<object> ReplayPlanAsync(BusinessAuditHostFixture fixture)
        {
            Assert.NotNull(sql); using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await using var connection = new SqlConnection(db.Database.GetDbConnection().ConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "SET STATISTICS XML ON; " + sql + " SET STATISTICS XML OFF;";
            command.Parameters.AddRange(parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
            await using var reader = await command.ExecuteReaderAsync(); var plans = new List<XDocument>();
            do { if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase)) while (await reader.ReadAsync()) plans.Add(XDocument.Parse(reader.GetString(0))); } while (await reader.NextResultAsync());
            Assert.NotEmpty(plans); var nodes = plans.SelectMany(plan => plan.Descendants()).ToArray();
            return new { planCount = plans.Count, physicalOperators = nodes.Where(node => node.Name.LocalName == "RelOp").GroupBy(node => node.Attribute("PhysicalOp")?.Value).ToDictionary(group => group.Key ?? "", group => group.Count()), actualRows = nodes.Where(node => node.Name.LocalName == "RunTimeCountersPerThread").Select(node => node.Attribute("ActualRows")?.Value).Where(value => value is not null).Distinct().ToArray(), warnings = nodes.Where(node => node.Name.LocalName == "Warnings").Select(node => node.ToString()).ToArray() };
        }
        public void OnCompleted() { } public void OnError(Exception error) { }
        public void Dispose() { active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }
}
