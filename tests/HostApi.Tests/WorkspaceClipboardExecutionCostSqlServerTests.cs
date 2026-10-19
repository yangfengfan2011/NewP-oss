using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占记录普通主体剪贴板单项、异构、最大页及OR分支的相同样本成本")]
    public async Task ClipboardExecution_RecordsOrdinarySingleMixedAndMaximumCosts()
    {
        var selectedSample = Environment.GetEnvironmentVariable("NEWP_CLIPBOARD_COST_SAMPLE") ?? "all";
        Assert.Contains(selectedSample, new[] { "all", "single-part", "mixed-three", "maximum-hundred", "mixed-or-true-copy" });
        var cost = new ClipboardCost();
        var lockClock = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(cost, lockClock, new FolderCopyTransactionClock(lockClock))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "执行成本来源");
        var folder = await Folder(fixture, admin, source, "异构文件夹");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "CLIPBOARD-COST-PART-000", ENAME = "成本零组件" } });
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents",
            new { position = source, values = new { ECODE = "CLIPBOARD-COST-DOCUMENT", ENAME = "成本图文档" } });
        var links = await Rows(fixture, "WorkspaceFolderContent");
        var sourceId = FolderId(source, links);
        var partLink = Assert.Single(links, row => Equals(row["PEID"], sourceId) && Equals(row["ChildModelKey"], "Part"));
        var documentLink = Assert.Single(links, row => Equals(row["PEID"], sourceId) && Equals(row["ChildModelKey"], "Document"));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IPlatformObjectIdGenerator>();
            var seed = await db.Set<Dictionary<string, object?>>("Part").AsNoTracking().SingleAsync();
            for (var index = 1; index < (selectedSample is "all" or "maximum-hundred" ? 100 : 1); index++)
            {
                var id = ids.NewId("Part");
                db.Set<Dictionary<string, object?>>("Part").Add(new(seed) { ["EID"] = id, ["ECODE"] = "CLIPBOARD-COST-PART-" + index.ToString("D3") });
                db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Add(new(partLink) { ["LID"] = ids.NewId("WorkspaceFolderContent"), ["CEID"] = id });
            }
            await db.SaveChangesAsync();
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ClipboardExecutionCost", "剪贴板执行成本",
            "clipboard-execution-cost", "剪贴板执行成本账号", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"),
            Point(fixture, "workspace.folder.cut"), Point(fixture, "workspace.folder.paste"), Point(fixture, "workspace.folder.removeSelected"));
        using var ordinary = actor.Client;
        string connectionString;
        using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        using var timing = new ClipboardSqlTiming(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        using var permissionQueries = new ClipboardPermissionQueries(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        var stage = Environment.GetEnvironmentVariable("NEWP_EXECUTION_COST_STAGE") ?? "candidate";
        Assert.Matches("^[a-zA-Z0-9-]+$", stage);
        var evidenceRoot = ClipboardCostEvidenceRoot();
        Directory.CreateDirectory(evidenceRoot);
        var samples = new[]
        {
            (Name: "single-part", Ids: new[] { (string)partLink["LID"]! }),
            (Name: "mixed-three", Ids: new[] { folder.LinkIds[^1], (string)partLink["LID"]!, (string)documentLink["LID"]! }),
            (Name: "maximum-hundred", Ids: (await Rows(fixture, "WorkspaceFolderContent")).Where(row => Equals(row["PEID"], sourceId)
                && Equals(row["ChildModelKey"], "Part")).Select(row => (string)row["LID"]!).ToArray())
        };
        foreach (var sample in samples)
        {
            if (selectedSample != "all" && sample.Name != selectedSample) continue;
            var target = await Folder(fixture, admin, root, "执行成本目标-" + sample.Name);
            var movedTarget = await Folder(fixture, admin, root, "执行成本移动-" + sample.Name);
            await Measure(sample.Name, "select-copy", sample.Ids.Length, () => Command(fixture, ordinary, "workspace.folder.copy",
                "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = sample.Ids }));
            await Measure(sample.Name, "paste-copy", sample.Ids.Length, () => Command(fixture, ordinary, "workspace.folder.paste",
                "/api/workspace/content/paste", new { mode = "copy", sourcePosition = source, linkIds = sample.Ids, targetPosition = target }));
            links = await Rows(fixture, "WorkspaceFolderContent");
            var copiedIds = links.Where(row => Equals(row["PEID"], FolderId(target, links))).Select(row => (string)row["LID"]!).ToArray();
            Assert.Equal(sample.Ids.Length, copiedIds.Length);
            await Measure(sample.Name, "select-cut", copiedIds.Length, () => Command(fixture, ordinary, "workspace.folder.cut",
                "/api/workspace/content/clipboard/cut/validate", new { mode = "cut", sourcePosition = target, linkIds = copiedIds }));
            await Measure(sample.Name, "paste-cut", copiedIds.Length, () => Command(fixture, ordinary, "workspace.folder.paste",
                "/api/workspace/content/paste", new { mode = "cut", sourcePosition = target, linkIds = copiedIds, targetPosition = movedTarget }));
            links = await Rows(fixture, "WorkspaceFolderContent");
            var movedIds = links.Where(row => Equals(row["PEID"], FolderId(movedTarget, links))).Select(row => (string)row["LID"]!).ToArray();
            Assert.Equal(sample.Ids.Length, movedIds.Length);
            await Measure(sample.Name, "remove", movedIds.Length, () => Command(fixture, ordinary, "workspace.folder.removeSelected",
                "/api/workspace/content/references/remove", new { position = movedTarget, linkIds = movedIds }));
        }
        if (selectedSample is not ("all" or "mixed-or-true-copy")) return;
        // 仅真副本动作允许时，选择仍可用，但正式引用粘贴保持原有拒绝。
        await Rule(fixture, admin, "ClipboardCostReferenceDenied", "part.part", (string)partLink["CEID"]!, "partCopy", "deny");
        var mixed = samples[1];
        await Measure("mixed-or-true-copy", "select-copy", mixed.Ids.Length, () => Command(fixture, ordinary, "workspace.folder.copy",
            "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = mixed.Ids }));
        var deniedTarget = await Folder(fixture, admin, root, "执行成本OR拒绝目标");
        await Measure("mixed-or-true-copy", "paste-reference-denied", mixed.Ids.Length, async () =>
        {
            using var denied = await Send(fixture, ordinary, "workspace.folder.paste", "paste",
                new { mode = "copy", sourcePosition = source, linkIds = mixed.Ids, targetPosition = deniedTarget });
            await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        });

        async Task Measure(string sample, string operation, int count, Func<Task> action)
        {
            cost.Begin(); timing.Begin(); lockClock.Begin(); permissionQueries.Begin();
            var watch = Stopwatch.StartNew();
            object database; object sqlTiming;
            try { await action(); }
            finally { watch.Stop(); database = cost.End(); sqlTiming = timing.End(); permissionQueries.End(); }
            var permissionPlans = await permissionQueries.ExplainAsync(connectionString);
            var evidence = new { stage, sample, operation, count, actor = "ordinary-limited-features", pooling = false,
                database, sqlTiming, elapsedMs = watch.Elapsed.TotalMilliseconds, graphLockWaitMs = lockClock.WaitMs,
                graphLockHoldMs = lockClock.HoldMs, transactionMs = lockClock.TransactionMs, permissionPlans };
            await File.WriteAllTextAsync(Path.Combine(evidenceRoot, $"clipboard-cost-{stage}-{sample}-{operation}.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{sample}/{operation}: count={count}, elapsedMs={watch.Elapsed.TotalMilliseconds:F2}, SQL={JsonSerializer.Serialize(database)}");
        }
    }

    private static string ClipboardCostEvidenceRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        return Path.Combine(root?.FullName ?? throw new InvalidOperationException("Repository root not found."), ".codex-temp", "issue-305");
    }

    // 按当前阶段捕获权限查询形状及次数，测量结束后重放每种形状一个样本；重放不混入运行时往返。
    private sealed class ClipboardPermissionQueries : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private readonly string database;
        private readonly List<IDisposable> subscriptions = [];
        private readonly Dictionary<string, (SqlParameter[] Parameters, int Calls)> queries = new(StringComparer.Ordinal);
        private bool active;
        public ClipboardPermissionQueries(string database)
        {
            this.database = database;
            all = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void Begin()
        {
            queries.Clear(); active = true;
        }
        public void End() => active = false;
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!active || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not SqlCommand command
                || command.Connection?.Database != database || !command.CommandText.Contains("DataScope", StringComparison.Ordinal)
                || !(command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                    || command.CommandText.TrimStart().StartsWith("SET NOCOUNT ON;", StringComparison.Ordinal)
                    && command.CommandText.Contains("Readable TABLE(", StringComparison.Ordinal))) return;
            if (queries.TryGetValue(command.CommandText, out var sample)) queries[command.CommandText] = (sample.Parameters, sample.Calls + 1);
            else queries.Add(command.CommandText, (command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray(), 1));
        }
        public async Task<object[]> ExplainAsync(string connectionString)
        {
            var evidence = new List<object>();
            foreach (var (sql, sample) in queries)
            {
                await using var connection = new SqlConnection(connectionString);
                var statistics = new List<string>();
                connection.InfoMessage += (_, args) => statistics.Add(args.Message);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON; " + sql + " SET STATISTICS XML OFF; SET STATISTICS IO OFF;";
                command.Parameters.AddRange(sample.Parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
                await using var reader = await command.ExecuteReaderAsync();
                var plans = new List<string>(); var returnedRows = 0;
                do
                {
                    var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
                    while (await reader.ReadAsync()) if (isPlan) plans.Add(reader.GetString(0)); else returnedRows++;
                } while (await reader.NextResultAsync());
                Assert.NotEmpty(plans); Assert.NotEmpty(statistics);
                evidence.Add(new { sql, sample.Calls, returnedRows, statistics, plans, timing = "after-request-representative-query-shape" });
            }
            Assert.NotEmpty(evidence);
            return evidence.ToArray();
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }
}
