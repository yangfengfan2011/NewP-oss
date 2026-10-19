using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>当前候选内隔离重放显式旧路径与正式绑定；补充原提交基线未采集的权限SQL逻辑读及计划。</summary>
public sealed class ExampleExecutionSqlPlanTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "先验证SQL统计观察器能捕获真实IO与实际计划")]
    public async Task PermissionCostObservation_CapturesLogicalReadsAndActualPlan()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        using var capture = new Capture { Active = true };
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.objects /* DataScope instrumentation */";
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) { }
        capture.Active = false;
        var sample = Assert.Single(capture.Samples);
        var observed = await ExplainAsync(database.ConnectionString, sample);
        Assert.NotEmpty(observed.Statistics);
        Assert.NotEmpty(observed.Plans);
    }

    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较相同Example权限查询的逻辑读与计划，不改变连接池设置")]
    public async Task ExamplePermissionQueries_KeepTheSameLogicalReadsAndPlans()
    {
        var phases = new Dictionary<string, Measurement[]>();
        foreach (var legacy in new[] { true, false })
        {
            using var capture = new Capture();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
                (_, options) => options.AddInterceptors(new EntityReadPlans(capture))), includeExample: true, legacyExampleExecutions: legacy);
            var http = new FeatureHttpTestRequests(fixture);
            using var admin = await fixture.LoginAdministratorAsync();
            var read = http.Example(); var create = http.Example("create"); var edit = http.Example("edit"); var delete = http.Example("delete");
            var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExamplePlan", "权限查询计划", "example-plan", "计划账号", read, create, edit, delete);
            using var ordinary = actor.Client;
            var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
            Assert.NotNull(fixture.Services.GetRequiredService<ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry>().FindByResourceKey("example.record")!.DisplayProjectionPlan);
            using (var scope = fixture.Services.CreateScope()) capture.ConnectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
            string? id = null;
            foreach (var operation in new[] { "create", "update", "delete" })
            {
                capture.Samples.Clear(); capture.EntityReads.Clear(); capture.Active = true;
                var feature = operation == "create" ? create : operation == "update" ? edit : delete;
                try
                {
                    using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature,
                        operation == "create" ? HttpMethod.Post : operation == "update" ? HttpMethod.Put : HttpMethod.Delete,
                        "/api/example/records" + (operation == "create" ? "" : "/" + id),
                        operation == "create" ? new { ECODE = "EXECUTION-PLAN", ENAME = "计划目标", RecordSource = "Manual" } : operation == "update" ? new { ENAME = "计划更新" } : null,
                        legacy ? null : plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record." + operation && item.FeaturePositions.Contains(feature.Position)));
                    Assert.True(response.StatusCode == (operation == "create" ? HttpStatusCode.Created : operation == "update" ? HttpStatusCode.OK : HttpStatusCode.NoContent), await response.Content.ReadAsStringAsync());
                    if (operation == "create") id = BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));
                }
                finally { capture.Active = false; }
                using var scope = fixture.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                var measurements = new List<Measurement>();
                foreach (var sample in capture.Samples)
                    measurements.Add(await ExplainAsync(db.Database.GetConnectionString()!, sample));
                measurements.AddRange(capture.EntityReads);
                Assert.NotEmpty(measurements);
                Assert.Contains(measurements, sample => sample.Statistics.Any(line => line.Contains("logical reads", StringComparison.OrdinalIgnoreCase) || line.Contains("逻辑读取", StringComparison.Ordinal)));
                var phase = (legacy ? "registered-legacy" : "execution-contract") + "-" + operation;
                phases[phase] = measurements.ToArray();
                await File.WriteAllTextAsync(Path.Combine(Root(), ".codex-temp", "issue-305", "example-permission-plan-" + phase + ".json"),
                    JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        foreach (var operation in new[] { "create", "update", "delete" })
        {
            var before = phases["registered-legacy-" + operation]; var after = phases["execution-contract-" + operation];
            Assert.Equal(before.Select(sample => sample.Sql), after.Select(sample => sample.Sql));
            // 逻辑读和实际计划保留原始证据逐项分析，不把一次读数固定成全局性能预算。
        }
    }

    private sealed record Measurement(string Sql, string[] Statistics, string[] Plans, int ReturnedRows)
    {
        public string Timing { get; init; } = "after-request-unchanged-permission-configuration";
    }
    private sealed record Sample(string Sql, SqlParameter[] Parameters);
    // 与现有Workspace成本资产同样观察SqlClient，覆盖EF和直接ADO命令，不记录参数或凭据。
    private sealed class Capture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private readonly List<IDisposable> subscriptions = [];
        internal bool Active;
        internal string ConnectionString = "";
        internal List<Sample> Samples { get; } = [];
        internal List<Measurement> EntityReads { get; } = [];
        internal Capture() => all = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active || !value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command) return;
            if (!command.CommandText.Contains("DataScope", StringComparison.Ordinal) || command.CommandText.Contains("[ExampleRecord]", StringComparison.Ordinal)
                || !command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)) return;
            Samples.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            Active = false;
            foreach (var subscription in subscriptions) subscription.Dispose();
            all.Dispose();
        }
    }
    // 业务对象查询必须在原查询前取证，delete之后不能把对象消失的0行当成删除前成本。
    private sealed class EntityReadPlans(Capture capture) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (capture.Active && command.CommandText.Contains("[ExampleRecord]", StringComparison.Ordinal)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
            {
                var sample = new Sample(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
                capture.EntityReads.Add((await ExplainAsync(capture.ConnectionString, sample)) with { Timing = "before-original-entity-read" });
            }
            return result;
        }
    }
    // 权限配置不被CRUD写入改变；按捕获的同SQL/参数定向重放只读查询，取得真实IO、返回规模与实际计划。
    // 请求本身的命令/连接成本保留原测量；诊断重放的往返不混入运行时成本。
    private static async Task<Measurement> ExplainAsync(string connectionString, Sample sample)
    {
        await using var connection = new SqlConnection(connectionString);
        var statistics = new List<string>();
        connection.InfoMessage += (_, args) => statistics.Add(args.Message);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON; " + sample.Sql + " SET STATISTICS XML OFF; SET STATISTICS IO OFF;";
        command.Parameters.AddRange(sample.Parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var plans = new List<string>();
        var rows = 0;
        do
        {
            var plan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
                if (plan) plans.Add(reader.GetString(0)); else rows++;
        } while (await reader.NextResultAsync());
        Assert.NotEmpty(plans);
        Assert.NotEmpty(statistics);
        return new(sample.Sql, statistics.ToArray(), plans.ToArray(), rows);
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
