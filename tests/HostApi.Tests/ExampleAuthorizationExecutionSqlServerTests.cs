using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ExampleAuthorizationExecutionSqlServerTests
{
    // 账号/角色都使用已迁移的真实管理来源，不能回退为仅功能资格。
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "实际账号与角色通用授权保持正式来源、原入口资格和完整保存")]
    public async Task ExistingIdentityAndRole_KeepFormalGenericManagement()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var catalog = fixture.Services.GetRequiredService<ICP.Platform.Authorization.Contracts.IDataScopeResourceCatalog>();
        Assert.All(new[] { "identity.account", "authorization.role" }, key =>
            Assert.Contains(plan.ActionDataAccessCatalog, declaration => declaration.Provider == "authorization.object.manage"
                && declaration.Resources.SequenceEqual([key])));
        using var admin = await fixture.LoginAdministratorAsync(); var http = new FeatureHttpTestRequests(fixture);
        var identity = plan.FeatureCatalog.First(point => point.BehaviorKey == "identity.account.authorize");
        var role = plan.FeatureCatalog.First(point => point.BehaviorKey == "authorization.role.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LegacyManagement", "既有通用管理",
            "legacy-management", "既有通用管理账号", http.Identity(), http.Role(), identity, role);
        using var ordinary = actor.Client;
        foreach (var target in new[] { (Key: "identity.account", Id: actor.AccountId, Point: identity), (Key: "authorization.role", Id: actor.RoleId, Point: role) })
        {
            var path = "/api/authorization/data-scope/objects/" + target.Key + "/" + target.Id;
            var source = OwnedResourceReadHttpRequests.ForExecution(fixture, target.Point, target.Key + ".authorization.manage", "authorization.object.manage", target.Key);
            using var eligible = await FeatureHttpTestRequests.SendAsync(ordinary, target.Point, HttpMethod.Get, path + "/eligibility", dataAccess: source);
            Assert.Equal(HttpStatusCode.OK, eligible.StatusCode);
            using var saved = await FeatureHttpTestRequests.SendAsync(ordinary, target.Point, HttpMethod.Put, path, new DataScopeObjectDto(target.Key, target.Id, []), source);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, target.Point, HttpMethod.Get, path, dataAccess: source);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, target.Point == identity ? role : identity, HttpMethod.Get, path, dataAccess: source);
            Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
        }
    }

    // 同一真实Example入口跨模块调用；先证明旧generic缺声明可通行的反例，再覆盖正式绑定和锁内保存。
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "Example通用授权绑定保留read+authorize与保存后失权成功")]
    public async Task ExampleManagement_RequiresCrossModuleBindingAndPreservesItsQualification()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeExample: true);
        var http = new FeatureHttpTestRequests(fixture); var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        using var admin = await fixture.LoginAdministratorAsync();
        var create = http.Example("create"); var manage = http.Example("authorize"); var inspect = http.Example("viewAuthorization");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/example/records",
            new { ECODE = "AUTHORIZATION-EXECUTION", ENAME = "通用授权执行", RecordSource = "Manual" },
            plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.create" && item.FeaturePositions.Contains(create.Position)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExampleManagementExecution", "跨模块授权",
            "example-management-execution", "跨模块授权账号", http.Example(), manage, inspect);
        using var ordinary = actor.Client;
        var path = "/api/authorization/data-scope/objects/example.record/" + id;
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + "/eligibility"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var binding = plan.ActionDataAccessCatalog.Single(item => item.FeaturePositions.Contains(manage.Position));
        var viewBinding = plan.ActionDataAccessCatalog.Single(item => item.FeaturePositions.Contains(inspect.Position));
        Assert.Equal("Authorization", binding.ProviderModuleId);
        foreach (var suffix in new[] { "/eligibility", "/catalog", "", "/subjects/user?page=1", "/explanation" })
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + suffix, dataAccess: binding);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + "/eligibility",
            dataAccess: plan.ActionDataAccessCatalog.Single(item => item.Execution == "example.record.create")))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path + "/eligibility", dataAccess: viewBinding))
            Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
        using (var wrongPosition = await FeatureHttpTestRequests.SendAsync(ordinary, http.Example(), HttpMethod.Get, path, dataAccess: binding))
            Assert.Equal(HttpStatusCode.Forbidden, wrongPosition.StatusCode);
        DataScopeObjectDto Config(string read, string authorize) => new("example.record", id,
            [new("ExecutionActor", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = read, ["authorize"] = authorize })]);
        using (var viewSave = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Put, path, Config("allow", "allow"), viewBinding))
            Assert.Equal(HttpStatusCode.BadRequest, viewSave.StatusCode);
        foreach (var denied in new[] { "authorize", "read" })
        {
            using var save = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                Config(denied == "read" ? "deny" : "allow", denied == "authorize" ? "deny" : "allow"), binding);
            Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, inspect, HttpMethod.Get, path + "/catalog", dataAccess: viewBinding);
            Assert.Equal(denied == "read" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var restore = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Config("allow", "allow"), binding))
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        using (var revoke = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, Config("deny", "deny"), binding))
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var after = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, path, dataAccess: binding);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }
}
