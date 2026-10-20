using System.Data.Common;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占记录标准读取/两种正式导出消费者及跨分片当前值的相同工作量")]
    public async Task StandardRead_RecordsMaximumPageAndMoreThanOneHundredAllCosts()
    {
        var commands = new CostCommands(); var connections = new CostConnections(); var reads = new ReadQuerySamples();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(commands, connections, reads)), includeExample: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture); var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var exampleRead = http.Example(); var exampleExport = http.Example("export", "list");
        var personRead = http.Organization("persons"); var personExport = http.Organization("persons", "export", "list");
        if (Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_BINDINGS_ONLY") == "1")
        {
            foreach (var source in new[] { ("example-list100", exampleRead), ("example-detail", exampleRead), ("example-export-current", exampleExport),
                ("person-list100", personRead), ("person-detail", personRead), ("person-export-all", personExport), ("position-options-current101", personRead) })
                output.WriteLine(source.Item1 + ": " + (ReadBindingFor(source.Item1, source.Item2)?.Id ?? "pre-migration-unbound"));
            Assert.NotNull(ReadBindingFor("example-list100", exampleRead)); Assert.NotNull(ReadBindingFor("example-detail", exampleRead));
            return;
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "StandardRead309", "标准读取成本",
            "standard-read309", "标准读取普通主体", exampleRead, exampleExport, personRead, personExport);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreatePrototypeAsync(
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies", new { ECODE = "READ-309-C", ENAME = "关联公司", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await CreatePrototypeAsync(http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "READ-309-D", ENAME = "关联部门", SortOrder = "0100" }));
        using var exampleCreated = await FeatureHttpTestRequests.SendAsync(admin, http.Example("create"), HttpMethod.Post, "/api/example/records",
            new { ECODE = "READ-309-000", ENAME = "READ-309-000", ReviewerUserEID = actor.AccountId, RecordSource = "Manual" },
            plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.create"));
        Assert.Equal(HttpStatusCode.Created, exampleCreated.StatusCode);
        var example = BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await exampleCreated.Content.ReadAsStringAsync()));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreatePrototypeAsync(
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "READ-309-000", ENAME = "READ-309-000", CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var position = BusinessAuditSqlServerHttpApiTests.Id(await CreatePrototypeAsync(
            http.Organization("positions", "create", "list.pageActions"), "/api/organization/positions", new { ENAME = "READ-309-000" }));
        string connectionString; string[] positionIds;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            connectionString = db.Database.GetConnectionString()!;
            await SeedAsync("ExampleRecord", example, "ECODE", 102);
            await SeedAsync("Person", person, "PersonNo", 102);
            positionIds = await SeedAsync("Position", position, "ENAME", 101);
            await db.SaveChangesAsync();

            // 容量准备复制真实API已持久化的合法形状；不把逐条创建/审计准备计入读取请求。
            async Task<string[]> SeedAsync(string model, string originalId, string code, int count)
            {
                var set = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model));
                var prototype = await set.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == originalId);
                var ids = new List<string> { originalId };
                for (var index = 1; index < count; index++)
                {
                    var id = model + "_" + Guid.NewGuid().ToString("N"); ids.Add(id);
                    var text = "READ-309-" + index.ToString("000");
                    var row = new Dictionary<string, object?>(prototype) { ["EID"] = id, [code] = text, ["ENAME"] = text };
                    if (row.ContainsKey("ECODE")) row["ECODE"] = text;
                    set.Add(row);
                }
                return ids.ToArray();
            }
        }
        await DenyAsync("example.record", "ECODE", "READ-309-101");
        await DenyAsync("organization.person", "PersonNo", "READ-309-101");
        await DenyAsync("organization.company", "EID", company);
        await DenyAsync("organization.position", "ENAME", "READ-309-100");
        if (Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_PREPARATION_ONLY") == "1") return;
        var stage = Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_COST_STAGE") ?? "candidate";
        var run = Path.Combine(FindRoot(), ".codex-temp", "issue-305", "309", "cost", stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var selected = (Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_COST_OPERATIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var measured = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in new[] { (Name: "example", Path: "/api/example/records", Read: exampleRead, Export: exampleExport, Id: example, Code: "ECODE"),
            (Name: "person", Path: "/api/organization/persons", Read: personRead, Export: personExport, Id: person, Code: "PersonNo") })
        {
            await MeasureAsync(resource.Name + "-list100", resource.Read, HttpMethod.Get, resource.Path + "?page=1&pageSize=100&keyword=READ-309", null, 100, false);
            await MeasureAsync(resource.Name + "-detail", resource.Read, HttpMethod.Get, resource.Path + "/" + resource.Id, null, 1, false);
            foreach (var mode in new[] { "current", "all" })
                await MeasureAsync(resource.Name + "-export-" + mode, resource.Export, HttpMethod.Post, resource.Path + "/export",
                    new StandardCrudExportRequest(mode, [new("code", resource.Code, "编码"), new("name", "ENAME", "名称"),
                        new("relation", resource.Name == "person" ? "CompanyId" : "ReviewerUserEID", "关联")],
                        new(PageIndex: 1, PageSize: 100, SortBy: resource.Code, SortDirection: "asc", Filters: new Dictionary<string, string?> { ["keyword"] = "READ-309" })),
                    mode == "all" ? 101 : 100, true);
        }
        await MeasureAsync("position-options-current101", personRead, HttpMethod.Get,
            "/api/organization/positions/options?pageSize=100&keyword=READ-309&currentValueIds=" + string.Join(',', positionIds), null, 100, false);

        Assert.Equal(selected.Count == 0 ? 9 : selected.Count, measured.Count);
        async Task<JsonElement> CreatePrototypeAsync(FeaturePoint feature, string path, object body)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path, body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        }
        async Task DenyAsync(string resource, string field, string value)
        {
            var key = "Read309_" + resource.Replace('.', '_');
            var rule = new DataScopeRuleDto(key, key, null, key, true, resource,
                new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
                [new("Subject_" + key, "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + key, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        ActionDataAccessRequirement? ReadBindingFor(string operation, FeaturePoint feature)
        {
            var provider = "standardCrud." + (operation.Contains("export", StringComparison.Ordinal) ? "export"
                : operation.EndsWith("detail", StringComparison.Ordinal) ? "read" : operation.Contains("options", StringComparison.Ordinal) ? "options" : "list");
            var resource = operation.StartsWith("example", StringComparison.Ordinal) ? "example.record"
                : operation.StartsWith("person", StringComparison.Ordinal) ? "organization.person" : "organization.position";
            return plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Provider == provider && item.Resources.Contains(resource) && item.FeaturePositions.Contains(feature.Position));
        }
        async Task MeasureAsync(string operation, FeaturePoint feature, HttpMethod method, string path, object? body, int expectedRows, bool workbook)
        {
            if (selected.Count > 0 && !selected.Contains(operation)) return;
            measured.Add(operation);
            var binding = ReadBindingFor(operation, feature);
            commands.Begin(); connections.Begin(); reads.Samples.Clear(); reads.Active = true; var watch = Stopwatch.StartNew();
            byte[] content;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, method, path, body, binding);
                Assert.True(response.StatusCode == HttpStatusCode.OK, operation + ": HTTP " + (int)response.StatusCode);
                content = await response.Content.ReadAsByteArrayAsync();
            }
            finally { watch.Stop(); reads.Active = false; commands.End(); connections.End(); }
            int returned;
            if (workbook)
            {
                using var archive = new ZipArchive(new MemoryStream(content));
                using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                returned = XDocument.Load(sheet).Descendants().Count(node => node.Name.LocalName == "row") - 1;
            }
            else
            {
                using var json = JsonDocument.Parse(content);
                returned = operation.EndsWith("detail", StringComparison.Ordinal) ? 1 : json.RootElement.GetProperty("items").GetArrayLength();
                if (operation.Contains("options", StringComparison.Ordinal)) Assert.Equal(101, json.RootElement.GetProperty("currentItems").GetArrayLength());
                else if (!operation.EndsWith("detail", StringComparison.Ordinal)) Assert.Equal(101, json.RootElement.GetProperty("total").GetInt32());
            }
            Assert.Equal(expectedRows, returned);
            // 数据与权限在请求后不变；只读重放独立连接，往返/耗时不计入原请求成本，不保存参数值。
            var observations = new List<ExampleExecutionSqlPlanTests.Measurement>();
            foreach (var sample in reads.Samples) observations.Add(await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: false));
            var evidence = new { stage, operation, preparedRowsPerExportResource = 102, readableRowsPerExportResource = 101, returned,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, responseBytes = content.Length, sqlCommands = commands.Items.Count,
                connections = connections.Items.ToArray(), commands = commands.Items.ToArray(), readPlans = observations };
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: SQL={commands.Items.Count}, returned={returned}, plans={observations.Count}, artifacts={run}");
        }
    }

    private sealed class ReadQuerySamples : DbCommandInterceptor
    {
        internal bool Active;
        internal List<ExampleExecutionSqlPlanTests.Sample> Samples { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Active && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                && new[] { "DataScope", "[ExampleRecord]", "[Person]", "[Position]", "[Company]", "[IdentityAccount]" }.Any(command.CommandText.Contains))
                Samples.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
