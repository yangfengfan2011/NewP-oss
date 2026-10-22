using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 复用既有SqlClient/EF/权限计划和锁仪器；此片只增加Part单对象的真实消费样本。
public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Part普通主体创建、读取和既有内部更新删除的单对象成本")]
    public async Task PartExecution_RecordsSingleObjectWriteCosts()
    {
        var selection = Environment.GetEnvironmentVariable("NEWP_PART_WRITE_COST_SAMPLE") ?? "all";
        Assert.Contains(selection, new[] { "all", "create" });
        var cost = new ClipboardCost();
        var clock = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(cost, clock, new FolderCopyTransactionClock(clock))), includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var folder = await Folder(fixture, admin, root, "Part写入成本父容器");
        var partRead = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var partCreate = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "part.part.create");
        var edit = RowPoint(fixture, "edit");
        var delete = RowPoint(fixture, "delete");
        Assert.Equal("workspace.folder.editObject", edit.BehaviorKey);
        Assert.Equal("workspace.folder.deleteObject", delete.BehaviorKey);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartExecutionCost", "Part执行成本",
            "part-execution-cost", "Part执行成本账号", partRead, partCreate, Point(fixture, "workspace.spaces.access"), edit, delete);
        using var ordinary = actor.Client;
        string connectionString;
        using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        var catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        using var timing = new ClipboardSqlTiming(catalog);
        using var permissions = new ClipboardPermissionQueries(catalog);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var stage = Environment.GetEnvironmentVariable("NEWP_EXECUTION_COST_STAGE") ?? "candidate";
        Assert.Matches("^[a-zA-Z0-9-]+$", stage);
        var run = Path.Combine(ClipboardCostEvidenceRoot(), "312", "part-cost-" + stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        await File.WriteAllTextAsync(Path.Combine(run, "candidate.json"), JsonSerializer.Serialize(new
        { stage, productionBaseline = "cc67b7bef103eaf3b9caecad655659f71548d60f", plan.FeatureCatalog, plan.ActionDataAccessCatalog }));
        var created = await Measure("create", partCreate, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "PART-312-COST", ENAME = "Part单对象成本", Description = "原字段" }, HttpStatusCode.Created);
        var id = created.GetProperty("fields").GetProperty("EID").GetString()!;
        if (selection == "create") return;
        await Measure("read", partRead, HttpMethod.Get, "/api/part/parts/" + id, null, HttpStatusCode.OK);
        await Command(fixture, admin, "workspace.folder.add", "/api/workspace/content/references",
            new { position = folder, target = new { modelKey = "Part", id } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part") && Equals(row["CEID"], id));
        var lid = (string)link["LID"]!;
        await Measure("update", edit, HttpMethod.Put, "/api/workspace/content/" + lid,
            new { position = folder, values = new { ECODE = "PART-312-COST", ENAME = "Part单对象已更新", Description = "新字段" } }, HttpStatusCode.OK);
        await Measure("delete", delete, HttpMethod.Delete, "/api/workspace/content/" + lid,
            new { position = folder }, HttpStatusCode.OK);

        async Task<JsonElement> Measure(string operation, FeaturePoint feature, HttpMethod method, string path, object? body, HttpStatusCode expected)
        {
            var execution = "part.part." + operation;
            // 只选择正式目录中的真实出现位置；baseline未迁移create时保留其原请求，不构造允许结果。
            var declaration = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == execution && item.FeaturePositions.Contains(feature.Position));
            cost.Begin(); timing.Begin(); clock.Begin(); permissions.Begin();
            var watch = Stopwatch.StartNew();
            object database; object sqlTiming; JsonElement payload;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, method, path, body, declaration);
                Assert.True(response.StatusCode == expected, operation + ": HTTP " + (int)response.StatusCode);
                payload = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsByteArrayAsync());
            }
            finally { watch.Stop(); database = cost.End(); sqlTiming = timing.End(); permissions.End(); }
            var permissionPlans = await permissions.ExplainAsync(connectionString);
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
            { stage, operation, objects = 1, ordinaryRoles = 1, directRules = 0, pooling = false, database, sqlTiming,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, graphLockWaitMs = clock.WaitMs,
                graphLockHoldMs = clock.HoldMs, transactionMs = clock.TransactionMs, permissionPlans }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: {JsonSerializer.Serialize(sqlTiming)}, artifacts={run}");
            return payload;
        }
    }
}
