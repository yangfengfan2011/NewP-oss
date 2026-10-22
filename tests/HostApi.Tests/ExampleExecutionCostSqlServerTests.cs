using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Example相同普通主体的单对象写入SQL成本")]
    public async Task ExampleCrud_RecordsSingleObjectWriteCost()
    {
        var commands = new CostCommands();
        var connections = new CostConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands, connections)), includeExample: true);
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Example();
        var create = http.Example("create");
        var edit = http.Example("edit");
        var delete = http.Example("delete");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExampleExecutionCost", "执行成本",
            "example-execution-cost", "执行成本账号", read, create, edit, delete);
        using var ordinary = actor.Client;
        var stage = Environment.GetEnvironmentVariable("NEWP_EXECUTION_COST_STAGE") ?? "candidate";
        var artifacts = Path.Combine(FindRoot(), ".codex-temp", "issue-305");
        Directory.CreateDirectory(artifacts);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        await File.WriteAllTextAsync(Path.Combine(artifacts, $"feature-catalog-{stage}.json"),
            JsonSerializer.Serialize(new { candidate = stage, plan.FeatureCatalog }, new JsonSerializerOptions { WriteIndented = true }));
        string? id = null;
        foreach (var operation in new[] { "create", "edit", "delete" })
        {
            commands.Begin(); connections.Begin();
            var watch = Stopwatch.StartNew();
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary,
                    operation == "create" ? create : operation == "edit" ? edit : delete,
                    operation == "create" ? HttpMethod.Post : operation == "edit" ? HttpMethod.Put : HttpMethod.Delete,
                    "/api/example/records" + (operation == "create" ? "" : "/" + id),
                    operation == "create" ? new { ECODE = "EXECUTION-COST", ENAME = "成本记录", RecordSource = "Manual" } :
                    operation == "edit" ? new { ENAME = "成本更新" } : null,
                    plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "example.record." + (operation == "edit" ? "update" : operation)
                        && item.FeaturePositions.Contains((operation == "create" ? create : operation == "edit" ? edit : delete).Position)));
                Assert.True(response.StatusCode == (operation == "create" ? HttpStatusCode.Created :
                    operation == "edit" ? HttpStatusCode.OK : HttpStatusCode.NoContent), await response.Content.ReadAsStringAsync());
                if (operation == "create") id = BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));
            }
            finally { watch.Stop(); commands.End(); connections.End(); }
            var evidence = new { stage, operation, objects = 1, grantedOrdinaryRoles = 1, directRules = 0,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, sqlCommands = commands.Items.Count,
                sqlExecuteMilliseconds = commands.Items.Sum(item => item.ExecuteMilliseconds),
                connections = connections.Items.ToArray(), commands = commands.Items.ToArray() };
            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(artifacts, $"example-cost-{stage}-{operation}.json"), json);
            output.WriteLine($"{stage}/{operation}: SQL={commands.Items.Count}, elapsedMs={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
