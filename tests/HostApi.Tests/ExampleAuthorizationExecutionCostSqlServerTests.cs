using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    // 同一普通主体和单对象逐请求采集；准备、授权与清理不混入通用端点请求成本。
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Example跨模块授权读取和完整保存成本")]
    public async Task ExampleAuthorization_RecordsSingleObjectManagementCost()
    {
        var commands = new CostCommands(); var connections = new CostConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(commands, connections)), includeExample: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
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
        var config = new DataScopeObjectDto("example.record", id,
            [new("CostAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]);
        var stage = Environment.GetEnvironmentVariable("NEWP_AUTHORIZATION_COST_STAGE") ?? "candidate";
        var run = Path.Combine(FindRoot(), ".codex-temp", "issue-305", "307", "cost", stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var binding = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.FeaturePositions.Contains(manage.Position));
        foreach (var operation in new[] { "eligibility", "catalog", "configuration", "subjects", "save" })
        {
            var suffix = operation switch { "configuration" or "save" => "", "subjects" => "/subjects/user?page=1", _ => "/" + operation };
            commands.Begin(); connections.Begin(); var watch = Stopwatch.StartNew();
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage,
                    operation == "save" ? HttpMethod.Put : HttpMethod.Get, path + suffix, operation == "save" ? config : null, binding);
                Assert.True(response.StatusCode == (operation == "save" ? HttpStatusCode.NoContent : HttpStatusCode.OK), $"{operation}: HTTP {(int)response.StatusCode}");
            }
            finally { watch.Stop(); commands.End(); connections.End(); }
            var evidence = new { stage, operation, objects = 1, directSubjectRowsBeforeSave = 0, grantedOrdinaryRoles = 1,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, sqlCommands = commands.Items.Count,
                sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds),
                authorizationLocks = commands.Items.Count(item => item.Sql.Contains("sp_getapplock", StringComparison.Ordinal)),
                lockExecuteMilliseconds = commands.Items.Where(item => item.Sql.Contains("sp_getapplock", StringComparison.Ordinal)).Sum(item => item.ExecuteMilliseconds),
                connections = connections.Items.ToArray(), commands = commands.Items.ToArray() };
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: SQL={commands.Items.Count}, elapsedMs={watch.Elapsed.TotalMilliseconds:F2}, artifacts={run}");
        }
    }
}
