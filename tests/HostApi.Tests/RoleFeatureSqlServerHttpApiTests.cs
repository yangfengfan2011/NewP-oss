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
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture,
            admin,
            "RoleCreator",
            "角色创建者",
            "role-creator",
            "普通角色创建者",
            create);
        using var ordinary = actor.Client;
        var role = await requests.CreateButtonAsync(ordinary, create, "/api/authorization/roles", new { Code = "GlobalCreated", Name = "普通主体全局创建" });
        using var wrongOperation = await requests.SendButtonAsync(ordinary, create, HttpMethod.Put,
            $"/api/authorization/roles/{role.GetProperty("id").GetString()}", new { Name = "不可编辑" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongOperation.StatusCode);
        var roleRoute = $"/api/authorization/roles/{role.GetProperty("id").GetString()}";
        await ExpectAsync(requests.SendButtonAsync(ordinary, requests.Role("edit"), HttpMethod.Put, roleRoute, new { Name = "未授编辑" }), HttpStatusCode.Forbidden);
        using var missingPosition = new HttpRequestMessage(HttpMethod.Post, "/api/authorization/roles")
        {
            Content = JsonContent.Create(new { Code = "MissingPosition", Name = "缺少位置" })
        };
        missingPosition.Headers.Add("X-NewP-Button", requests.Declaration(create.BehaviorKey, create).Id);
        await ExpectAsync(ordinary.SendAsync(missingPosition), HttpStatusCode.Forbidden);
        await requests.GrantAsync(admin, actor.RoleId, create, requests.Role("edit"));
        await ExpectAsync(requests.SendButtonAsync(ordinary, requests.Role("edit"), HttpMethod.Put, roleRoute, new { Name = "获授全局编辑", IsEnabled = true }), HttpStatusCode.OK);
        await ExpectAsync(requests.SendButtonAsync(ordinary, requests.Role("edit", "detail.pageActions"), HttpMethod.Put, roleRoute, new { Name = "另一位置" }), HttpStatusCode.Forbidden);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, requests.Role("edit"), HttpMethod.Delete, roleRoute), HttpStatusCode.Forbidden);
        using var protectedRole = await requests.SendButtonAsync(ordinary, create, HttpMethod.Post,
            "/api/authorization/roles", new { Code = "LocalAdmin", Name = "不可创建" });
        Assert.Equal(HttpStatusCode.BadRequest, protectedRole.StatusCode);
        var problem = await protectedRole.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("受保护的角色身份不能通过普通管理创建。", problem.GetProperty("title").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "普通成员管理员全局分配、角色来源并集与在途完成")]
    public async Task OrdinaryMembershipManager_UsesGlobalActionAndCurrentSourcesWhileAdmittedWriteCompletes()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var roles = new Dictionary<string, string>();
        foreach (var code in new[] { "ManagerA", "ManagerB", "TargetRead" })
        {
            var role = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = code, Name = code });
            roles[code] = role.GetProperty("id").GetString()!;
        }
        var member = http.Role("userRoles");
        await http.GrantAsync(admin, roles["ManagerA"], member);
        await http.GrantAsync(admin, roles["ManagerB"], member);
        await http.GrantAsync(admin, roles["TargetRead"], http.Workbench("authorization.workbench.read"));
        var manager = await http.CreateButtonAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), "/api/identity/accounts", new { ECODE = "member-manager", ENAME = "普通成员管理员" });
        var target = await http.CreateButtonAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), "/api/identity/accounts", new { ECODE = "member-target", ENAME = "另一普通账号" });
        var managerId = manager.GetProperty("account").GetProperty("eid").GetString();
        var targetId = target.GetProperty("account").GetProperty("eid").GetString();
        await ExpectAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, member, HttpMethod.Put, $"/api/authorization/users/{managerId}/roles", new { RoleCodes = new[] { "ManagerA", "ManagerB" } }), HttpStatusCode.NoContent);
        await HttpTestActors.PrepareOrganizationAsync(admin, http, manager.GetProperty("account"));
        await HttpTestActors.PrepareOrganizationAsync(admin, http, target.GetProperty("account"));
        using var ordinary = await fixture.LoginAsync("member-manager", manager.GetProperty("temporaryCredential").GetString()!);
        using var targetClient = await fixture.LoginAsync("member-target", target.GetProperty("temporaryCredential").GetString()!);
        var targetRoute = $"/api/authorization/users/{targetId}/roles";
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get, "/api/authorization/users/candidates?query=member-target"), HttpStatusCode.OK);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get, "/api/authorization/roles/query?pageIndex=1&pageSize=1",
            dataAccess: http.Declaration("authorization.role.list", member)), HttpStatusCode.OK);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get, targetRoute), HttpStatusCode.OK);
        await ExpectAsync(http.SendButtonAsync(ordinary, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = "NoCreate", Name = "不自动获得创建" }), HttpStatusCode.Forbidden);
        await ExpectAsync(http.SendButtonAsync(ordinary, http.Role("userRoles", "detail.pageActions"), HttpMethod.Put, targetRoute, new { RoleCodes = new[] { "TargetRead" } }), HttpStatusCode.Forbidden);
        await ExpectAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, member, HttpMethod.Put, targetRoute, new { RoleCodes = new[] { "TargetRead" } }), HttpStatusCode.NoContent);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(targetClient, http.Workbench("authorization.workbench.read"), HttpMethod.Get, "/api/authorization/workbench/catalog"), HttpStatusCode.OK);
        await ExpectAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, member, HttpMethod.Put, targetRoute, new { RoleCodes = new[] { "LocalAdmin" } }), HttpStatusCode.BadRequest);
        var administrator = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        await ExpectAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, member, HttpMethod.Put, $"/api/authorization/users/{administrator.GetProperty("id").GetString()}/roles", new { RoleCodes = Array.Empty<string>() }), HttpStatusCode.BadRequest);
        await ExpectAsync(http.SendButtonAsync(ordinary, http.Workbench("authorization.workbench.save"), HttpMethod.Put, $"/api/authorization/workbench/roles/{roles["TargetRead"]}", new { positions = Array.Empty<string>() }), HttpStatusCode.Forbidden);
        // 移除一个用户角色来源不会提前撤销另一来源，原Cookie继续消费当前关系。
        await ExpectAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, member, HttpMethod.Put, $"/api/authorization/users/{managerId}/roles", new { RoleCodes = new[] { "ManagerB" } }), HttpStatusCode.NoContent);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get, targetRoute), HttpStatusCode.OK);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType == "user.roles.changed" && change.TargetId == targetId)
            { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        var inFlight = new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, member, HttpMethod.Put, targetRoute, new { RoleCodes = Array.Empty<string>() });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await http.GrantAsync(admin, roles["ManagerB"]);
        }
        finally { release.TrySetResult(); }
        await ExpectAsync(inFlight, HttpStatusCode.NoContent);
        fixture.Fault.AfterRegister = null;
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get, targetRoute), HttpStatusCode.Forbidden);
        await ExpectAsync(FeatureHttpTestRequests.SendAsync(targetClient, http.Workbench("authorization.workbench.read"), HttpMethod.Get, "/api/authorization/workbench/catalog"), HttpStatusCode.Forbidden);
    }

    private static async Task ExpectAsync(Task<HttpResponseMessage> pending, HttpStatusCode status)
    {
        using var response = await pending;
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
