using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeSqlCostTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "数据范围过滤的真实SQL下推、往返、分页返回量与实际执行计划")]
    public async Task PersonRead_RecordsRepresentativeQueryCost()
    {
        var commands = new Commands();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Cost", "成本", "scope-cost", "成本主体", read)).Client;
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("Scope_Cost", "Scope_Cost", null, "成本规则", true, "organization.person",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("Blocked")),
            [new("Scope_Cost_Subject", "all", null, 100, new Dictionary<string, string> { ["read"] = "deny" })],
            [new("Scope_Cost_Allow", "Scope_Cost", "Scope_Cost", "子例外", true, "organization.person",
                new("attribute", "PersonNo", "equal", JsonSerializer.SerializeToElement("SCOPE-COST-0")),
                [new("Scope_Cost_Allow_Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow" })], [])]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Cost", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var created = 0;
        string? objectAllowed = null;
        foreach (var size in new[] { 32, 128 })
        {
            for (; created < size; created++)
            {
                var person = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
                    new { PersonNo = "SCOPE-COST-" + created, ENAME = created % 2 == 0 ? "Blocked" : "Visible", EmploymentStatus = "Active", IsEnabled = true });
                if (created == 2) objectAllowed = BusinessAuditSqlServerHttpApiTests.Id(person);
            }
            if (size == 32)
            {
                var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
                var config = new DataScopeObjectDto("organization.person", objectAllowed!,
                    [new("Scope_Cost_Object", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
                using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                    $"/api/authorization/data-scope/objects/organization.person/{objectAllowed}", config);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
            commands.Captured.Clear(); commands.Enabled = true;
            var watch = Stopwatch.StartNew();
            JsonElement result;
            try { result = await FeatureHttpTestRequests.ReadAsync(ordinary, read, "/api/organization/persons?page=2&pageSize=7"); }
            finally { commands.Enabled = false; }
            watch.Stop();
            Assert.Equal(size / 2 + 3, result.GetProperty("total").GetInt32());
            Assert.Equal(7, result.GetProperty("items").GetArrayLength());
            Assert.DoesNotContain(result.GetProperty("items").EnumerateArray(), row => row.GetProperty("ENAME").GetString() == "Blocked"
                && row.GetProperty("PersonNo").GetString() is not ("SCOPE-COST-0" or "SCOPE-COST-2"));
            var queries = commands.Captured.Where(command => command.Sql.Contains("[Person]", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, queries.Length);
            Assert.All(queries, command => {
                Assert.Contains("[ENAME]", command.Sql); Assert.Contains("[PersonNo]", command.Sql);
                Assert.Contains("[DataScopeObjectSubject]", command.Sql); Assert.Contains("[DataScopeObjectAction]", command.Sql);
            });
            Assert.Single(queries, command => command.Sql.Contains("OFFSET", StringComparison.Ordinal));
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var plans = new List<object>();
            foreach (var query in queries) plans.Add(await ActualPlanAsync(db.Database.GetConnectionString()!, query));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                personRows = size + 1,
                endpointMilliseconds = watch.Elapsed.TotalMilliseconds,
                databaseRoundTrips = commands.Captured.Count,
                businessQueries = queries.Length,
                applicationPersonRows = 7,
                applicableRuleRows = 2,
                applicableSubjectRows = 2,
                objectSubjectRows = 1,
                objectActionRows = 1,
                plans
            }));
        }
    }

    // 重放捕获的只读参数化查询并读取 STATISTICS XML，保留实际算子行数而非估算代替。
    private static async Task<object> ActualPlanAsync(string connectionString, Captured query)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SET STATISTICS XML ON; " + query.Sql;
        foreach (var parameter in query.Parameters) command.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
        await using var reader = await command.ExecuteReaderAsync();
        string? xml = null;
        do
        {
            while (await reader.ReadAsync())
                if (reader.FieldCount == 1 && reader.GetValue(0) is string text && text.Contains("<ShowPlanXML", StringComparison.Ordinal)) xml = text;
        } while (await reader.NextResultAsync());
        Assert.NotNull(xml);
        var plan = XDocument.Parse(xml);
        return new
        {
            query.Sql,
            operators = plan.Descendants().Where(element => element.Name.LocalName == "RelOp").Select(element => new
            {
                physical = (string?)element.Attribute("PhysicalOp"),
                actualRows = Counter(element, "ActualRows"),
                actualRowsRead = Counter(element, "ActualRowsRead"),
                logicalReads = Counter(element, "ActualLogicalReads")
            }).ToArray()
        };
    }

    private static long? Counter(XElement operation, string name)
    {
        var values = operation.Elements().Where(child => child.Name.LocalName == "RunTimeInformation").SelectMany(child => child.Elements())
            .Select(child => (long?)child.Attribute(name)).ToArray();
        return values.Any(value => value.HasValue) ? values.Sum() : null;
    }

    private sealed record Captured(string Sql, SqlParameter[] Parameters);
    private sealed class Commands : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal List<Captured> Captured { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        private void Record(DbCommand command)
        {
            if (Enabled) Captured.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
    }
}
