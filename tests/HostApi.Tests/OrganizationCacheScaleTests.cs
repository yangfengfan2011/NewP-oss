using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "完整一万人员一千部门的缓存与正式局部写入成本实测")]
    public async Task CompleteOrganizationScale_RecordsActualCostsWithoutInventedThresholds()
    {
        var costs = new ScaleConnectionCosts();
        var commands = new SubtreeCommands();
        var commitWindow = new MeasuredCommitWindow();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<OrganizationCache, ScaleCache>();
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(costs, commands, commitWindow));
        });
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        var departments = new[] { prepared.Department }.Concat(Enumerable.Range(1, 999).Select(_ => Guid.NewGuid().ToString("N"))).ToArray();
        var positions = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
        var evidence = new List<object>();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var executor = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            await executor.ExecuteAsync(["Department", "Position", "Person"], token =>
            {
                for (var index = 1; index < departments.Length; index++)
                    db.Set<Dictionary<string, object?>>("Department").Add(new()
                    {
                        ["EID"] = departments[index], ["ECODE"] = $"D{index:0000}", ["ENAME"] = $"完整档案部门{index:0000}",
                        ["CompanyId"] = prepared.Company, ["ParentDepartmentId"] = departments[(index - 1) / 4],
                        ["SortOrder"] = index.ToString("0000"), ["IsEnabled"] = index % 17 != 0
                    });
                for (var index = 0; index < positions.Length; index++)
                    db.Set<Dictionary<string, object?>>("Position").Add(new()
                    { ["EID"] = positions[index], ["ENAME"] = $"完整档案岗位{index}", ["Description"] = new string('岗', 120), ["IsEnabled"] = index % 3 != 0 });
                for (var index = 1; index < 10000; index++)
                    db.Set<Dictionary<string, object?>>("Person").Add(new()
                    {
                        ["EID"] = Guid.NewGuid().ToString("N"), ["PersonNo"] = $"P{index:00000}", ["ENAME"] = $"完整档案人员{index:00000}",
                        ["CompanyId"] = prepared.Company, ["DepartmentId"] = departments[index % 1000], ["PositionId"] = positions[index % 10],
                        ["EmploymentStatus"] = "FullTime", ["IsEnabled"] = index % 13 != 0,
                        ["BirthDate"] = new DateOnly(1990, 6, 15), ["EmploymentStartedAt"] = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        ["EmploymentEndedAt"] = index % 13 == 0 ? new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc) : null
                    });
                return ValueTask.FromResult(true);
            });
            db.ChangeTracker.Clear();
            await using var sql = new SqlConnection(db.Database.GetConnectionString());
            await sql.OpenAsync();
            await using var version = sql.CreateCommand();
            version.CommandText = "SELECT @@VERSION";
            evidence.Add(new { stage = "environment", os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount,
                sqlServer = (string)(await version.ExecuteScalarAsync())!, configuration = "Release", scope = "one disposable LocalDB catalog; full current authoring records" });
        }
        var cache = (ScaleCache)fixture.Services.GetRequiredService<OrganizationCache>();
        var snapshot = await cache.ReadAsync();
        Assert.Equal(1000, snapshot.Departments.Count);
        Assert.Equal(10000, snapshot.Persons.Count);
        var depth = 0;
        foreach (var department in snapshot.Departments.Values)
        {
            var current = department; var value = 1;
            while (current.GetValueOrDefault("ParentDepartmentId") is string parent) { current = snapshot.Departments[parent]; value++; }
            depth = Math.Max(depth, value);
        }
        evidence.Add(new { stage = "shape", companies = snapshot.Companies.Count, departments = snapshot.Departments.Count,
            persons = snapshot.Persons.Count, positions = snapshot.Positions.Count, depth,
            disabledDepartments = snapshot.Departments.Values.Count(row => Equals(row["IsEnabled"], false)),
            disabledPersons = snapshot.Persons.Values.Count(row => Equals(row["IsEnabled"], false)),
            boundPersons = snapshot.PersonByAccount.Count,
            companyFields = snapshot.Companies.Values.First().Count, departmentFields = snapshot.Departments.Values.First().Count,
            personFields = snapshot.Persons.Values.First().Count, positionFields = snapshot.Positions.Values.First().Count });

        var startupCache = new OrganizationCache(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        var beforeMemory = GC.GetTotalMemory(true);
        var beforeAllocated = GC.GetTotalAllocatedBytes(true);
        await Measure("initial-full-cache-load", () => startupCache.InitializeAsync().AsTask());
        var retained = GC.GetTotalMemory(true) - beforeMemory;
        evidence.Add(new { stage = "memory", retainedManagedBytesDelta = retained, allocatedBytes = GC.GetTotalAllocatedBytes(true) - beforeAllocated,
            processWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
            note = "second cache of same real SQL dataset; delta includes retained EF/runtime noise; initial loader retained for measurement" });
        Assert.Equal(10000, (await startupCache.ReadAsync()).Persons.Count);

        await Measure("person-local-update", async () =>
        {
            using var response = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/persons/{prepared.Person}", new { ENAME = "局部更新档案" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
        await Measure("subtree-disable", () => SetOrganizationStateAsync(admin, http, "departments", prepared.Department, false));
        await Measure("subtree-enable", () => SetOrganizationStateAsync(admin, http, "departments", prepared.Department, true));
        await Measure("independent-one-person-refresh", () => cache.RefreshAsync([new("Person", prepared.Person)]).AsTask());

        await Measure("concurrent-warm-read", () => Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            for (var index = 0; index < 1000; index++) Assert.Equal(10000, (await cache.ReadAsync()).Persons.Count);
        }))));
        Assert.Empty(commands.Sql);

        commitWindow.Enabled = true;
        var writeWatch = Stopwatch.StartNew();
        var write = SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{prepared.Person}", new { ENAME = "提交发布等待" });
        await commitWindow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waits = Enumerable.Range(0, 32).Select(async _ =>
        {
            var watch = Stopwatch.StartNew();
            var current = await cache.ReadAsync();
            Assert.Equal("提交发布等待", current.Persons[prepared.Person]["ENAME"]);
            return watch.Elapsed.TotalMilliseconds;
        }).ToArray();
        Assert.All(waits, task => Assert.False(task.IsCompleted));
        var held = writeWatch.Elapsed.TotalMilliseconds;
        commitWindow.Release.TrySetResult();
        using (var response = await write) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var samples = await Task.WhenAll(waits);
        evidence.Add(new { stage = "controlled-commit-publication-window", readers = samples.Length, writeTotalMs = writeWatch.Elapsed.TotalMilliseconds,
            reachedBarrierMs = held, readerMinMs = samples.Min(), readerMaxMs = samples.Max(), readerMeanMs = samples.Average(),
            note = "TCS deliberately holds actual committed transaction before cache publication; measured waits include injected scheduling hold, not natural latency budget" });
        var path = Path.Combine(FindScaleRepositoryRoot(), ".codex-temp", "issue214-scale.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var item in evidence) output.WriteLine(JsonSerializer.Serialize(item));
        GC.KeepAlive(startupCache);

        async Task Measure(string operation, Func<Task> action)
        {
            costs.Reset(); commands.Sql.Clear(); cache.Updates.Clear(); costs.Enabled = true; commands.Enabled = true;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            try { await action(); }
            finally { watch.Stop(); costs.Enabled = false; commands.Enabled = false; }
            evidence.Add(new { stage = operation, wallMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                roundTrips = costs.Samples.Sum(s => s.RoundTrips), rows = costs.Samples.Sum(s => s.Rows), bytes = costs.Samples.Sum(s => s.Bytes),
                sqlCpuMs = costs.Samples.Sum(s => s.Cpu), logicalReads = costs.Samples.Sum(s => s.LogicalReads), physicalReads = costs.Samples.Sum(s => s.PhysicalReads),
                sqlCommands = commands.Sql.Count, fullDepartmentReads = commands.Sql.Count(sql => sql.Contains("FROM [dbo].[Department]", StringComparison.Ordinal) && !sql.Contains("WHERE", StringComparison.Ordinal)),
                fullPersonReads = commands.Sql.Count(sql => sql.Contains("FROM [dbo].[Person]", StringComparison.Ordinal) && !sql.Contains("WHERE", StringComparison.Ordinal)),
                cacheUpdates = cache.Updates.ToArray(),
                note = "includes independent SQL session observation overhead; Department/Person CRUD uniqueness reads are baseline, not cache rebuild" });
        }
    }

    private static string FindScaleRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln"))) current = current.Parent;
        return current!.FullName;
    }

    private sealed class MeasuredCommitWindow : DbTransactionInterceptor
    {
        public bool Enabled;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return;
            Enabled = false; Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ScaleCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        public List<object> Updates { get; } = [];
        protected override async ValueTask<OrganizationSnapshot> ApplyAsync(OrganizationSnapshot snapshot, IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var result = await base.ApplyAsync(snapshot, changes, token);
            Updates.Add(new { records = changes.Count, elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated });
            return result;
        }
    }

    private sealed class ScaleConnectionCosts : DbConnectionInterceptor
    {
        public bool Enabled;
        public List<SqlCost> Samples { get; } = [];
        private readonly Dictionary<DbConnection, long[]> _started = [];
        public void Reset() { Samples.Clear(); _started.Clear(); }
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return;
            var sql = (SqlConnection)connection;
            // 池化连接的第一次命令会执行sp_reset_connection；先完成重置，再观察同一session的计数基线。
            await using (var reset = sql.CreateCommand()) { reset.CommandText = "SELECT 1"; await reset.ExecuteScalarAsync(cancellationToken); }
            _started.Add(connection, await ReadCost(sql));
            sql.StatisticsEnabled = true;
            sql.ResetStatistics();
        }
        public override async ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (!_started.Remove(connection, out var before)) return result;
            var sql = (SqlConnection)connection;
            var after = await ReadCost(sql);
            var statistics = sql.RetrieveStatistics();
            Assert.True(after.Zip(before).All(pair => pair.First >= pair.Second), "SQL session counters reset during measured operation.");
            Samples.Add(new(Convert.ToInt64(statistics["ServerRoundtrips"]), Convert.ToInt64(statistics["SelectRows"]), Convert.ToInt64(statistics["BytesReceived"]),
                after[0] - before[0], after[1] - before[1], after[2] - before[2]));
            return result;
        }
        private static async Task<long[]> ReadCost(SqlConnection target)
        {
            await using var observer = new SqlConnection(target.ConnectionString);
            await observer.OpenAsync();
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT cpu_time, logical_reads, reads FROM sys.dm_exec_sessions WHERE session_id=@id";
            command.Parameters.AddWithValue("@id", target.ServerProcessId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return Enumerable.Range(0, 3).Select(index => Convert.ToInt64(reader.GetValue(index))).ToArray();
        }
    }
}
