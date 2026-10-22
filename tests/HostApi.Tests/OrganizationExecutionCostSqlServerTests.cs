using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Organization完整关系树及四个专属候选来源的既有SQL形状")]
    public async Task OrganizationSources_RecordTreeAndCurrentValueCosts()
    {
        using var capture = new OrganizationCostCapture();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var personRead = http.Organization("persons"); var departmentRead = http.Organization("departments");
        var tree = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "tree:organization.structure");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Organization310Cost", "组织来源成本",
            "organization310-cost", "组织普通主体", personRead, departmentRead, tree);
        using var ordinary = actor.Client;
        var company = await CreateAsync("companies", new { ECODE = "COST310-C", ENAME = "COST310 Company", SortOrder = "0100" });
        var department = await CreateAsync("departments", new { CompanyId = company, ECODE = "COST310-D", ENAME = "COST310 Department", SortOrder = "0100" });
        string connectionString; string[] companyIds; string[] departmentIds; string admissionCompany;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            connectionString = db.Database.GetConnectionString()!;
            var admissionPerson = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person")).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "AccountEID") == actor.AccountId);
            admissionCompany = Assert.IsType<string>(admissionPerson["CompanyId"]);
            companyIds = await ExpandAsync("Company", company);
            departmentIds = await ExpandAsync("Department", department);
            await db.SaveChangesAsync();

            async Task<string[]> ExpandAsync(string model, string original)
            {
                var set = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model));
                var prototype = await set.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == original);
                var ids = new List<string> { original };
                for (var index = 1; index < 101; index++)
                {
                    var id = model + "_" + Guid.NewGuid().ToString("N"); ids.Add(id);
                    set.Add(new(prototype) { ["EID"] = id, ["ECODE"] = "COST310-" + index.ToString("000"),
                        ["ENAME"] = "COST310 " + model + " " + index.ToString("000") });
                }
                return ids.ToArray();
            }
        }
        // 保留正式规则编译成本但不改变代表样本的读取范围；权限语义拒绝在独立反例中证明。
        foreach (var resource in new[] { "organization.company", "organization.department", "organization.person" })
        {
            var key = "Organization310Cost_" + resource.Replace('.', '_');
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + key,
                new DataScopeRuleDto(key, key, null, key, true, resource,
                    new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("NOT-IN-COST310-SAMPLE")),
                    [new(key + "All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        var stage = Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_EXECUTION_COST_STAGE") ?? "candidate";
        var run = Path.Combine(FindRoot(), ".codex-temp", "issue-305", "310", "cost", stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        await File.WriteAllTextAsync(Path.Combine(run, "feature-catalog.json"), JsonSerializer.Serialize(new { FeatureCatalog = plan.FeatureCatalog }));
        var selected = (Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_EXECUTION_COST_OPERATIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var source in new[]
        {
            ("person-company", personRead, "organization.person.company.lookup", "/api/organization/person-lookups/companies?pageSize=100&keyword=COST310&currentValueIds=" + string.Join(',', companyIds)),
            ("person-department", personRead, "organization.person.department.lookup", "/api/organization/person-lookups/departments?companyId=" + company + "&pageSize=100&keyword=COST310&currentValueIds=" + string.Join(',', departmentIds)),
            ("department-company", departmentRead, "organization.department.company.lookup", "/api/organization/department-lookups/companies?pageSize=100&keyword=COST310&currentValueIds=" + string.Join(',', companyIds)),
            ("department-parent", departmentRead, "organization.department.parent.lookup", "/api/organization/department-lookups/parents?companyId=" + company + "&pageSize=100&keyword=COST310&currentValueIds=" + string.Join(',', departmentIds))
        })
            await MeasureAsync(source.Item1, source.Item2, source.Item3, source.Item4, body =>
            { Assert.Equal(100, body.GetProperty("items").GetArrayLength()); Assert.Equal(101, body.GetProperty("currentItems").GetArrayLength()); return 100; });
        foreach (var operation in new[] { "roots", "children", "path", "search" })
        {
            var suffix = operation switch { "children" => "&nodeKey=company:" + company, "path" => "&nodeKey=department:" + department,
                "search" => "&keyword=COST310%20Department&page=1&pageSize=100", _ => "" };
            await MeasureAsync("tree-" + operation, tree, "organization.structure.read", "/api/trees/organization.structure/" + operation + "?scope=global" + suffix,
                body => { var count = operation == "search" ? body.GetProperty("items").GetArrayLength() : body.GetArrayLength();
                    Assert.Equal(operation == "roots" ? 102 : operation == "children" ? 101 : operation == "path" ? 2 : 100, count);
                    if (operation == "roots") Assert.Equal(companyIds.Append(admissionCompany).Select(id => "company:" + id).Order(),
                        body.EnumerateArray().Select(node => node.GetProperty("nodeKey").GetString()).Order());
                    if (operation == "search") Assert.Equal(101, body.GetProperty("total").GetInt32()); return count; });
        }

        async Task<string> CreateAsync(string tab, object body)
        {
            var feature = http.Organization(tab, "create", "list.pageActions");
            var binding = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == "organization." + (tab == "companies" ? "company" : "department") + ".create" && item.FeaturePositions.Contains(feature.Position));
            using var response = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, "/api/organization/" + tab, body, binding);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));
        }
        async Task MeasureAsync(string operation, FeaturePoint feature, string execution, string path, Func<JsonElement, int> check)
        {
            if (selected.Count != 0 && !selected.Contains(operation)) return;
            var binding = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == execution && item.FeaturePositions.Contains(feature.Position));
            capture.Begin(); var watch = Stopwatch.StartNew(); JsonElement body;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding);
                Assert.True(response.StatusCode == HttpStatusCode.OK, operation + ": HTTP " + (int)response.StatusCode);
                body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            }
            finally { watch.Stop(); capture.Active = false; }
            var returned = check(body);
            var samples = capture.Samples.ToArray();
            var observations = new List<ExampleExecutionSqlPlanTests.Measurement>();
            foreach (var sample in samples) observations.Add(await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: false));
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
            { stage, operation, sampleCompanyRows = 101, sampleDepartmentRows = 101, admissionCompanyRows = 1, admissionCompany, admissionDepartmentRows = 1, admissionPersonRows = 1, currentIds = operation.StartsWith("tree", StringComparison.Ordinal) ? 0 : 101,
                returned, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, sqlCommands = capture.Commands.Count,
                commands = capture.Commands.ToArray(), connectionEvents = capture.ConnectionEvents.ToArray(), readPlans = observations }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: SQL={capture.Commands.Count}, returned={returned}, plans={observations.Count}, artifacts={run}");
        }
    }

    // 观察SqlClient而非只观察EF，覆盖关系树既有直接ADO命令；只读回放在请求窗口结束后执行。
    private sealed class OrganizationCostCapture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private readonly List<IDisposable> subscriptions = [];
        internal bool Active;
        internal List<string> Commands { get; } = [];
        internal List<string> ConnectionEvents { get; } = [];
        internal List<ExampleExecutionSqlPlanTests.Sample> Samples { get; } = [];
        internal OrganizationCostCapture() => all = DiagnosticListener.AllListeners.Subscribe(this);
        internal void Begin() { Commands.Clear(); ConnectionEvents.Clear(); Samples.Clear(); Active = true; }
        public void OnNext(DiagnosticListener listener)
        { if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active) return;
            if (value.Key.EndsWith("WriteConnectionOpenAfter", StringComparison.Ordinal) || value.Key.EndsWith("WriteConnectionCloseAfter", StringComparison.Ordinal))
                ConnectionEvents.Add(value.Key);
            if (!value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command) return;
            Commands.Add(command.CommandText);
            if ((command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || command.CommandText.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                && new[] { "DataScope", "[Company]", "[Department]", "Reachable AS" }.Any(command.CommandText.Contains))
                Samples.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { Active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }
}
