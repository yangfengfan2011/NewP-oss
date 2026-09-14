using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class RoleFeatureSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "普通角色按真实创建位置管理并拒绝未授动作")]
    public async Task OrdinaryCreator_UsesRegisteredPositionWithoutAdministratorMembership()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var requests = new FeatureHttpTestRequests(fixture);
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "authorization.role.create");
        var manager = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/authorization/roles", new { Code = "RoleCreator", Name = "角色创建者" });
        using var accountResponse = await admin.PostAsJsonAsync("/api/identity/accounts", new { ECODE = "role-creator", ENAME = "普通角色创建者" });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("account").GetProperty("eid").GetString();
        var assign = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.role.userRoles" && point.ActionScope == "list.rowActions");
        using (var assigned = await FeatureHttpTestRequests.SendAsync(admin, assign, HttpMethod.Put, $"/api/authorization/users/{accountId}/roles", new { RoleCodes = new[] { "RoleCreator" } }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        await requests.GrantAsync(admin, manager.GetProperty("id").GetString()!, create);
        using var ordinary = await fixture.LoginAsync("role-creator", account.GetProperty("temporaryCredential").GetString()!);
        var role = await FeatureHttpTestRequests.CreateAsync(ordinary, create, "/api/authorization/roles", new { Code = "GlobalCreated", Name = "普通主体全局创建" });
        using var wrongOperation = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Put,
            $"/api/authorization/roles/{role.GetProperty("id").GetString()}", new { Name = "不可编辑" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongOperation.StatusCode);
        using var protectedRole = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/authorization/roles", new { Code = "LocalAdmin", Name = "不可创建" });
        Assert.Equal(HttpStatusCode.BadRequest, protectedRole.StatusCode);
    }
}
