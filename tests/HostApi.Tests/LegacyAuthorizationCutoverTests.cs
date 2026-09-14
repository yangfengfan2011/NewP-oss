using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
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
        using var ordinary = await PrepareBusinessAndOrdinaryAsync(fixture, http);
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
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, http.Role(), HttpMethod.Get, "/api/authorization/roles"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await http.GrantAsync(fixture.Administrator, roleId, http.Role());
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, http.Role(), HttpMethod.Get, "/api/authorization/roles"))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var assigned = await GrantsAsync(fixture, roleId);
        Assert.NotEmpty(assigned);
        await fixture.StopAsync();
        await fixture.StartAsync();
        await AssertLegacyEmptyAsync(fixture);
        Assert.Equal(assigned, await GrantsAsync(fixture, roleId));
        await fixture.StopAsync();
        var unchanged = await FeatureSqlMeasurement.CaptureAsync(fixture, "legacy-cutover-restart", () => fixture.StartAsync());
        Assert.DoesNotContain(System.Text.Json.JsonSerializer.SerializeToElement(unchanged).GetProperty("plans").EnumerateArray(), plan =>
            plan.GetProperty("statementType").GetString() is "INSERT" or "UPDATE" or "DELETE"
            && plan.GetProperty("indexes").EnumerateArray().Any(index => index.GetProperty("table").GetString() is "[Permission]" or "[RolePermission]"));
        var afterRestart = await PreservedRowsAsync(fixture);
        Assert.All(preserved, row => Assert.Contains(row, afterRestart));
        using var response = await FeatureHttpTestRequests.SendAsync(ordinary, http.Role(),
            HttpMethod.Get, "/api/authorization/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpClient> PrepareBusinessAndOrdinaryAsync(FeatureCatalogHostFixture fixture, FeatureHttpTestRequests http)
    {
        var admin = fixture.Administrator;
        var company = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "CUT", ENAME = "保留公司", SortOrder = "0100" });
        var department = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "CUT-D", ENAME = "保留部门", CompanyId = company.GetProperty("fields").GetProperty("EID").GetString(), SortOrder = "0100" });
        await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "CUT-P", ENAME = "保留人员", CompanyId = company.GetProperty("fields").GetProperty("EID").GetString(),
                DepartmentId = department.GetProperty("fields").GetProperty("EID").GetString(), EmploymentStatus = "Active", IsEnabled = true });
        var account = await FeatureHttpTestRequests.CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "cutover-user", ENAME = "切换用户" });
        var userId = account.GetProperty("account").GetProperty("eid").GetString();
        using (var assigned = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put,
            $"/api/authorization/users/{userId}/roles", new { RoleCodes = new[] { "cutover-role" } }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var password = account.GetProperty("temporaryCredential").GetString()!;
        var client = await fixture.LoginAsync("cutover-user", password);
        try
        {
            var current = await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
            using var changed = await client.PostAsJsonAsync("/api/identity/me/password-change", new
            { CurrentPassword = password, NewPassword = password + "-ready", ExpectedRevision = current.GetProperty("revision").GetInt64() });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            return client;
        }
        catch { client.Dispose(); throw; }
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
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // 仅读取本夹具真实业务与历史记录；固定表名不接受外部输入，历史行不修改。
        foreach (var table in new[] { "Company", "Department", "Person", "BusinessChange", "__NewPMigrationHistory" })
        {
            await using var command = new SqlCommand($"SELECT * FROM [dbo].[{table}] FOR JSON PATH, INCLUDE_NULL_VALUES", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var json = new System.Text.StringBuilder();
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            using var document = JsonDocument.Parse(json.ToString());
            var rows = document.RootElement.EnumerateArray().Select(row => table + ":" + row.GetRawText()).ToArray();
            Assert.NotEmpty(rows);
            result.AddRange(rows);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
}
