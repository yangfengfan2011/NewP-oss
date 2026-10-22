using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占记录普通主体添加引用与权限来源读取的原两处SQL、关键权限重放和锁区间")]
    public async Task ReferenceExecution_RecordsAddAndPermissionSourceCosts()
    {
        var cost = new ClipboardCost(); var clock = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(cost, clock, new FolderCopyTransactionClock(clock))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var parent = await Folder(fixture, admin, root, "引用成本父");
        var target = await Folder(fixture, admin, root, "引用成本目标");
        var rows = await Rows(fixture, "WorkspaceFolderContent");
        var parentId = FolderId(parent, rows); var targetId = FolderId(target, rows);
        var add = Point(fixture, "workspace.folder.add"); var view = Point(fixture, "workspace.spaces.access");
        var declaration = Assert.Single(fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>().ActionDataAccessCatalog,
            item => item.Execution == "workspace.content.authorization.view" && item.Surface == "read" && item.FeaturePositions.Contains(view.Position));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReferenceCost", "引用成本主体",
            "reference-cost", "引用成本主体", Point(fixture, "workspace.spaces.access"), add, view);
        using var ordinary = actor.Client;
        string connectionString;
        using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        var catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        using var timing = new ClipboardSqlTiming(catalog); using var permissions = new ClipboardPermissionQueries(catalog);
        var evidence = Environment.GetEnvironmentVariable("NEWP_REFERENCE_COST_ARTIFACT_ROOT") ?? throw new InvalidOperationException("Reference cost artifact root required.");
        Directory.CreateDirectory(evidence);
        await Measure("add", () => FeatureHttpTestRequests.SendAsync(ordinary, add, HttpMethod.Post, "/api/workspace/content/references",
            new { position = parent, target = new { modelKey = "WorkspaceFolder", id = targetId } }));
        var added = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["PEID"], parentId) && Equals(row["CEID"], targetId));
        var query = Uri.EscapeDataString(JsonSerializer.Serialize(parent, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Measure("permissions-catalog", () => FeatureHttpTestRequests.SendAsync(ordinary, view, HttpMethod.Get,
            "/api/workspace/content/" + added["LID"] + "/permissions/catalog?queryScope=" + query, dataAccess: declaration));

        async Task Measure(string sample, Func<Task<HttpResponseMessage>> action)
        {
            cost.Begin(); timing.Begin(); clock.Begin(); permissions.Begin(); var watch = Stopwatch.StartNew();
            object database; object sqlTiming;
            try
            {
                using var response = await action();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            }
            finally { watch.Stop(); database = cost.End(); sqlTiming = timing.End(); permissions.End(); }
            var permissionPlans = await permissions.ExplainAsync(connectionString);
            await File.WriteAllTextAsync(Path.Combine(evidence, sample + ".json"), JsonSerializer.Serialize(new
            {
                sample, actor = "ordinary-limited-features", workload = new { folders = 3, initialEdges = 2, addedEdges = sample == "add" ? 0 : 1, selectedTargets = 1 },
                database, sqlTiming, elapsedMs = watch.Elapsed.TotalMilliseconds, graphLockWaitMs = clock.WaitMs,
                graphLockHoldMs = clock.HoldMs, transactionMs = clock.TransactionMs, permissionPlans
            }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{sample}: SQL={JsonSerializer.Serialize(database)}");
        }
    }
}