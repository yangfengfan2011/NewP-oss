using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureCatalogHostLifecycleTests
{
    [EnvironmentGatedFact("NEWP_RUN_HOST_AUTH_RESTART_INTEGRATION", "真实Example声明经正式Host重启执行删除、重引、移动、复制及准备失败保护")]
    public async Task RealAuthoring_RestartsPreserveIdentityAndPermanentlyRemoveDeletedGrants()
    {
        var declarations = Declarations();
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync(declarations);
        var http = new FeatureHttpTestRequests(fixture.Catalog);
        var role = await FeatureHttpTestRequests.CreateAsync(fixture.Administrator, http.Role("create"), "/api/authorization/roles", new { Code = "lifecycle-reader", Name = "目录生命周期" });
        var roleId = role.GetProperty("id").GetString()!;
        var oldLeaf = Point(fixture, "sync-leaf");
        var oldRoot = Point(fixture, "sync-root");
        await http.GrantAsync(fixture.Administrator, roleId, oldLeaf);
        var before = await SnapshotAsync(fixture);
        await fixture.StopAsync();

        // 名称与排序只影响展示，数据库完整身份、EID和分配均保持原值。
        declarations[0]!["title"] = "改名后的入口";
        declarations[0]!["order"] = 999;
        fixture.SetDeclarations(declarations);
        await fixture.StartAsync();
        Assert.Equal(before, await SnapshotAsync(fixture));
        Assert.Equal(oldRoot.Position, Point(fixture, "sync-root").Position);
        await AssertReadAsync(fixture, oldLeaf, HttpStatusCode.OK);
        await fixture.StopAsync();

        // 缺失真实执行端的完整候选在同步之前失败，不能把失败解释为删除。
        var invalid = Declarations();
        invalid.RemoveAt(2);
        invalid[0]!["behaviorKey"] = "example.missing.binding";
        fixture.SetDeclarations(invalid);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("FeatureEndpointBindings.Validate", fixture.LastError);
        Assert.Equal(before, await SnapshotAsync(fixture));

        // 完整候选的字典输入准备失败，同样不能清理拟删除的分支。
        fixture.SetDeclarations(new JsonArray(declarations[0]!.DeepClone(), declarations[3]!.DeepClone()));
        var dictionary = Path.Combine(Path.Combine(fixture.HostRoot, "modules", "Example"), "metadata", "dictionaries", "Navigation.dic.json");
        var content = await File.ReadAllTextAsync(dictionary);
        await File.WriteAllTextAsync(dictionary, "{");
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PLUGIN_ACTIVATION_MODULE_PLAN_FAILED", fixture.LastError);
        Assert.Equal(before, await SnapshotAsync(fixture));
        await File.WriteAllTextAsync(dictionary, content);

        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.Key is "sync-branch" or "sync-leaf");
        await using (var db = fixture.CreateContext())
        {
            var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
            var ids = await grants.Where(row => EF.Property<string>(row, "RoleId") == roleId)
                .Select(row => EF.Property<string>(row, "FunctionPointId")).ToArrayAsync();
            Assert.Single(ids);
            Assert.Equal(oldRoot.Position, await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"))
                .Where(row => EF.Property<string>(row, "EID") == ids[0]).Select(row => EF.Property<string>(row, "PositionKey")).SingleAsync());
        }
        await AssertReadAsync(fixture, oldLeaf, HttpStatusCode.Forbidden);
        await fixture.StopAsync();
        fixture.SetDeclarations(declarations);
        await fixture.StartAsync();
        Assert.Equal(oldLeaf.Position, Point(fixture, "sync-leaf").Position);
        await AssertRoleHasOnlyRootAsync(fixture, roleId, oldRoot);
        await AssertReadAsync(fixture, oldLeaf, HttpStatusCode.OK); // LocalAdmin重新收敛，普通角色旧分配仍为空。
        await http.GrantAsync(fixture.Administrator, roleId, oldLeaf);
        await fixture.StopAsync();

        // 跨分支移动与复制生成新位置，普通角色不自动取得它们。
        declarations[2]!["parentKey"] = "sync-other";
        declarations.Add(Declaration("sync-copy", "action", "sync-other"));
        fixture.SetDeclarations(declarations);
        await fixture.StartAsync();
        Assert.NotEqual(oldLeaf.Position, Point(fixture, "sync-leaf").Position);
        await using (var db = fixture.CreateContext())
        {
            var selected = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"))
                .Where(row => EF.Property<string>(row, "RoleId") == roleId).Select(row => EF.Property<string>(row, "FunctionPointId")).ToArrayAsync();
            var positions = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"))
                .Where(row => selected.Contains(EF.Property<string>(row, "EID"))).Select(row => EF.Property<string>(row, "PositionKey")).ToArrayAsync();
            Assert.DoesNotContain(Point(fixture, "sync-leaf").Position, positions);
            Assert.DoesNotContain(Point(fixture, "sync-copy").Position, positions);
            Assert.Contains(oldRoot.Position, positions);
        }
        await AssertReadAsync(fixture, Point(fixture, "sync-copy"), HttpStatusCode.OK);
    }

    private static JsonArray Declarations() => [Declaration("sync-root", "navigation"), Declaration("sync-branch", "tab", "sync-root"), Declaration("sync-leaf", "action", "sync-branch"), Declaration("sync-other", "navigation")];
    internal static JsonObject Declaration(string key, string kind, string? parent = null) => new()
    {
        ["key"] = key, ["kind"] = kind, ["title"] = key, ["parentKey"] = parent,
        ["behaviorKey"] = "example.runtime-workload.read"
    };
    private static FeaturePoint Point(FeatureCatalogHostFixture fixture, string key) => fixture.Catalog.Single(point => point.ModuleId == "Example" && point.Key == key);
    private static async Task AssertReadAsync(FeatureCatalogHostFixture fixture, FeaturePoint point, HttpStatusCode expected)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, point, HttpMethod.Get, "/api/example/runtime-workload/columns");
        Assert.Equal(expected, response.StatusCode);
    }
    private static async Task AssertRoleHasOnlyRootAsync(FeatureCatalogHostFixture fixture, string roleId, FeaturePoint root)
    {
        await using var db = fixture.CreateContext();
        var id = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"))
            .Where(row => EF.Property<string>(row, "RoleId") == roleId).Select(row => EF.Property<string>(row, "FunctionPointId")).SingleAsync();
        Assert.Equal(root.Position, await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"))
            .Where(row => EF.Property<string>(row, "EID") == id).Select(row => EF.Property<string>(row, "PositionKey")).SingleAsync());
    }
    private static async Task<string[]> SnapshotAsync(FeatureCatalogHostFixture fixture)
    {
        await using var db = fixture.CreateContext();
        var points = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint")).AsNoTracking()
            .Select(row => new { Id = EF.Property<string>(row, "EID"), Key = EF.Property<string>(row, "PositionKey") }).ToArrayAsync();
        var grants = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).AsNoTracking()
            .Select(row => EF.Property<string>(row, "EID") + ":" + EF.Property<string>(row, "RoleId") + ":" + EF.Property<string>(row, "FunctionPointId")).ToArrayAsync();
        return points.Select(point => point.Id + ":" + point.Key).Concat(grants).Order(StringComparer.Ordinal).ToArray();
    }
}
