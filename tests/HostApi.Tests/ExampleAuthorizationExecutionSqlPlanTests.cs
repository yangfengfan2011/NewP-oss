using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class ExampleExecutionSqlPlanTests
{
    // SQL Server可把空集合折叠为Constant Scan；有实际计划但没有表IO不是观察器失效。
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "核实constant-false查询的零行无表IO计划")]
    public async Task PermissionPlanObservation_RecordsConstantScanWithoutInventingIo()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        var sample = await ExplainAsync(database.ConnectionString, new("SELECT object_id FROM sys.objects WHERE 0 = 1", []), requireIo: false);
        Assert.Equal("constant-scan:rows=0:no-table-io", ReadIo(sample));
    }

    // 独立诊断比较当前候选的受控旧/新路径；保存会改变ACL，所有样本均在原SELECT前取得。
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "同数据时点比较Example授权关键读取的实际IO/计划/行数")]
    public async Task ExampleAuthorization_KeepPermissionReadPlansAndTransactionalVisibility()
    {
        var run = Path.Combine(Root(), ".codex-temp", "issue-305", "307", "plans", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var samples = new Dictionary<string, AuthorizationMeasurement[]>();
        foreach (var legacy in new[] { true, false })
        {
            var observer = new AuthorizationReadPlans();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
                (_, options) => options.AddInterceptors(observer)), includeExample: true, legacyExampleAuthorization: legacy);
            using (var scope = fixture.Services.CreateScope())
                observer.ConnectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
            using var admin = await fixture.LoginAdministratorAsync();
            var http = new FeatureHttpTestRequests(fixture); var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
            var create = http.Example("create"); var manage = http.Example("authorize");
            using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/example/records",
                new { ECODE = "AUTHORIZATION-COST", ENAME = "授权成本", RecordSource = "Manual" },
                plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.create" && item.FeaturePositions.Contains(create.Position)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync()));
            var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExampleAuthorizationCost", "授权执行成本",
                "example-authorization-cost", "授权成本账号", http.Example(), manage, http.Example("viewAuthorization"));
            using var ordinary = actor.Client;
            var path = "/api/authorization/data-scope/objects/example.record/" + id;
            var binding = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.FeaturePositions.Contains(manage.Position));
            foreach (var operation in new[] { "eligibility", "catalog", "configuration", "subjects", "save" })
            {
                observer.Samples.Clear(); observer.Active = true;
                try
                {
                    var suffix = operation switch { "configuration" or "save" => "", "subjects" => "/subjects/user?page=1", _ => "/" + operation };
                    using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, operation == "save" ? HttpMethod.Put : HttpMethod.Get,
                        path + suffix, operation == "save" ? new DataScopeObjectDto("example.record", id,
                            [new("CostAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]) : null, binding);
                    Assert.True(response.StatusCode == (operation == "save" ? HttpStatusCode.NoContent : HttpStatusCode.OK),
                        $"Authorization diagnostic {operation} returned {(int)response.StatusCode}.");
                }
                finally { observer.Active = false; }
                var stage = (legacy ? "registered-legacy" : "execution-contract") + "-" + operation;
                samples[stage] = observer.Samples.ToArray();
                Assert.NotEmpty(samples[stage]);
                Assert.All(samples[stage], sample => Assert.Empty(sample.PendingPermissionModels));
                await File.WriteAllTextAsync(Path.Combine(run, stage + ".json"), JsonSerializer.Serialize(samples[stage], new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        var summary = new List<object>();
        foreach (var operation in new[] { "eligibility", "catalog", "configuration", "subjects", "save" })
        {
            var before = samples["registered-legacy-" + operation]; var after = samples["execution-contract-" + operation];
            Assert.Equal(before.Select(sample => sample.Read.Sql), after.Select(sample => sample.Read.Sql));
            Assert.Equal(before.Select(sample => sample.Read.ReturnedRows), after.Select(sample => sample.Read.ReturnedRows));
            Assert.Equal(before.Select(sample => sample.InTransaction), after.Select(sample => sample.InTransaction));
            Assert.Equal(before.Select(sample => ReadIo(sample.Read)), after.Select(sample => ReadIo(sample.Read)));
            Assert.Equal(before.Select(sample => PlanShape(sample.Read)), after.Select(sample => PlanShape(sample.Read)));
            summary.Add(new { operation, beforeQueries = before.Length, afterQueries = after.Length,
                beforeRows = before.Sum(sample => sample.Read.ReturnedRows), afterRows = after.Sum(sample => sample.Read.ReturnedRows),
                sourceTransactionQueries = after.Count(sample => sample.InTransaction),
                beforeIo = before.Select(sample => ReadIo(sample.Read)).ToArray(), afterIo = after.Select(sample => ReadIo(sample.Read)).ToArray() });
        }
        await File.WriteAllTextAsync(Path.Combine(run, "comparison.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record AuthorizationMeasurement(Measurement Read, bool InTransaction, string Isolation, string[] PendingPermissionModels);

    // 只取EF实际执行的权限/目标SELECT；不重放锁、DML或带未提交ACL变化的视图。
    private sealed class AuthorizationReadPlans : DbCommandInterceptor
    {
        internal bool Active;
        internal string ConnectionString = "";
        internal List<AuthorizationMeasurement> Samples { get; } = [];
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (!Active || !command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                || !(command.CommandText.Contains("DataScope", StringComparison.Ordinal) || command.CommandText.Contains("[ExampleRecord]", StringComparison.Ordinal))) return result;
            var pending = data.Context?.ChangeTracker.Entries().Where(entry => entry.Metadata.Name.Contains("DataScope", StringComparison.Ordinal)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).Select(entry => entry.Metadata.Name).ToArray() ?? [];
            if (pending.Length != 0) throw new InvalidOperationException("Permission plan replay cannot represent pending ACL writes.");
            var sample = new Sample(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
            Samples.Add(new((await ExplainAsync(ConnectionString, sample, requireIo: false)) with { Timing = "before-original-permission-or-target-read" },
                command.Transaction is not null, command.Transaction?.IsolationLevel.ToString() ?? "none", pending));
            return result;
        }
    }

    // SQL Server安装语言不同，保留原始IO并只比较表、扫描和逻辑读取，拒绝空解析假通过。
    private static string ReadIo(Measurement sample)
    {
        var rows = sample.Statistics.SelectMany(line => Regex.Matches(line,
            "(?:表“(?<table>[^”]+)”|Table '(?<table>[^']+)').*?(?:扫描计数|Scan count) (?<scan>\\d+).*?(?:逻辑读取次数|logical reads) (?<reads>\\d+)", RegexOptions.IgnoreCase)
            .Cast<Match>()).Select(match => match.Groups["table"].Value + ":" + match.Groups["scan"].Value + ":" + match.Groups["reads"].Value).ToArray();
        if (rows.Length == 0)
        {
            Assert.Empty(sample.Statistics);
            Assert.Equal(0, sample.ReturnedRows);
            var nodes = sample.Plans.SelectMany(plan => XDocument.Parse(plan).Descendants()).ToArray();
            Assert.DoesNotContain(nodes, node => node.Name.LocalName == "Object");
            Assert.Contains(nodes, node => node.Name.LocalName == "RelOp" && node.Attribute("PhysicalOp")?.Value == "Constant Scan");
            return "constant-scan:rows=0:no-table-io";
        }
        return string.Join(";", rows);
    }

    // 排除时长和缓存噪声，比较实际物理算子、对象/索引和运行时读取规模。
    private static string PlanShape(Measurement sample) => string.Join(";", sample.Plans.SelectMany(plan => XDocument.Parse(plan).Descendants())
        .Where(node => node.Name.LocalName is "RelOp" or "Object" or "RunTimeCountersPerThread")
        .Select(node => node.Name.LocalName + ":" + string.Join(",", node.Attributes().Where(attribute => attribute.Name.LocalName is
            "PhysicalOp" or "Schema" or "Table" or "Index" or "ActualRows" or "ActualRowsRead" or "ActualExecutions")
            .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal).Select(attribute => attribute.Name.LocalName + "=" + attribute.Value))));
}
