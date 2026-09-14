using System.Net;
using System.Text.Json.Nodes;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class LegacyAuthorizationCutoverTests
{
    [EnvironmentGatedFact("NEWP_RUN_HOST_AUTH_RESTART_INTEGRATION", "真实Host首次切换只清旧授权，准备失败保留原数据，普通角色重配跨重启保持")]
    public async Task PreparedHost_ClearsLegacyAssignments_AndRestartPreservesNewGrants()
    {
        var declarations = new JsonArray(FeatureCatalogHostLifecycleTests.Declaration("cutover", "navigation"));
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync(declarations);
        var http = new FeatureHttpTestRequests(fixture.Catalog);
        var role = await FeatureHttpTestRequests.CreateAsync(fixture.Administrator, http.Role("create"),
            "/api/authorization/roles", new { Code = "cutover-role", Name = "切换角色" });
        var roleId = role.GetProperty("id").GetString()!;
        await fixture.StopAsync();
        await using (var db = fixture.CreateContext())
        {
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Permission")).Add(new()
            {
                ["EID"] = "Permission_cutover", ["ECODE"] = "Legacy.Cutover", ["ENAME"] = "旧授权",
                ["ModuleId"] = "Example"
            });
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RolePermission")).Add(new()
            {
                ["EID"] = "RolePermission_cutover", ["RoleId"] = roleId,
                ["PermissionId"] = "Permission_cutover", ["GrantedAt"] = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var preserved = await PreservedRowsAsync(fixture);
        var invalid = declarations.DeepClone().AsArray();
        invalid[0]!["behaviorKey"] = "example.missing.binding";
        fixture.SetDeclarations(invalid);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("FeatureEndpointBindings.Validate", fixture.LastError);
        await using (var db = fixture.CreateContext())
            Assert.True(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RolePermission"))
                .AnyAsync(row => EF.Property<string>(row, "EID") == "RolePermission_cutover"));
        Assert.Equal(preserved, await PreservedRowsAsync(fixture));

        fixture.SetDeclarations(declarations);
        await fixture.StartAsync();
        await AssertLegacyEmptyAsync(fixture);
        Assert.Equal(preserved, await PreservedRowsAsync(fixture));
        await http.GrantAsync(fixture.Administrator, roleId, http.Role());
        var assigned = await GrantsAsync(fixture, roleId);
        Assert.NotEmpty(assigned);
        await fixture.StopAsync();
        await fixture.StartAsync();
        await AssertLegacyEmptyAsync(fixture);
        Assert.Equal(assigned, await GrantsAsync(fixture, roleId));
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, http.Role(),
            HttpMethod.Get, "/api/authorization/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertLegacyEmptyAsync(FeatureCatalogHostFixture fixture)
    {
        await using var db = fixture.CreateContext();
        foreach (var model in new[] { "Permission", "RolePermission" })
            Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AnyAsync());
    }

    private static async Task<string[]> GrantsAsync(FeatureCatalogHostFixture fixture, string roleId)
    {
        await using var db = fixture.CreateContext();
        return await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"))
            .Where(row => EF.Property<string>(row, "RoleId") == roleId)
            .OrderBy(row => EF.Property<string>(row, "EID"))
            .Select(row => EF.Property<string>(row, "EID") + ":" + EF.Property<string>(row, "FunctionPointId"))
            .ToArrayAsync();
    }

    private static async Task<string[]> PreservedRowsAsync(FeatureCatalogHostFixture fixture)
    {
        await using var db = fixture.CreateContext();
        var result = new List<string>();
        foreach (var model in new[] { "Role", "UserRole", "IdentityAccount" })
        {
            var rows = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking().ToArrayAsync();
            result.AddRange(rows.Select(row => model + ":" + System.Text.Json.JsonSerializer.Serialize(row.OrderBy(pair => pair.Key))));
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
}
