using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class IdentityRoleAuthorizationExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "两类generic对象管理消费既有authorize来源并保持read与authorize下限")]
    public async Task ActualIdentityAndRoleManagementSources_KeepQualificationAndRejectCrossedBindings()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var accountFeature = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "identity.account.authorize");
        var roleFeature = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.role.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Object311Formal", "正式对象授权主体",
            "object311-formal", "普通正式对象授权主体", accountFeature, roleFeature);
        using var ordinary = actor.Client;
        var role = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles",
            new { Code = "Object311TargetRole", Name = "正式授权角色" });
        var account = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "object311-formal-target", ENAME = "正式授权账号" });
        foreach (var (resource, id, feature, tag) in new[]
        {
            ("authorization.role", role.GetProperty("id").GetString()!, roleFeature, "Role"),
            ("identity.account", account.GetProperty("account").GetProperty("eid").GetString()!, accountFeature, "Account")
        })
        {
            var path = "/api/authorization/data-scope/objects/" + resource + "/" + id;
            var binding = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == resource + ".authorize" && item.FeaturePositions.Contains(feature.Position));
            Assert.Equal("featureActions", binding.Surface);
            Assert.Null(binding.Provider);
            Assert.Equal(new[] { "authorize" }, binding.Actions);
            // 第一次合法操作直接保存；不凭此前点击或eligibility结果放行。
            using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Put, path,
                Configuration("allow", "allow"), binding)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            using (var read = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.Single((await read.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("subjects").EnumerateArray());
            }
            var wrongResource = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding ==
                (resource == "authorization.role" ? "identity.account" : "authorization.role") + ".authorize");
            using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: wrongResource))
                Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Put, path, Configuration("allow", "allow")))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            await ConfigureAsync("deny", "allow");
            using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding))
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            await ConfigureAsync("allow", "deny");
            using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Put, path, Configuration("allow", "allow"), binding))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            DataScopeObjectDto Configuration(string read, string authorize) => new(resource, id,
                [new("Object311" + tag, "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = read, ["authorize"] = authorize, ["delete"] = "deny" })]);
            async Task ConfigureAsync(string read, string authorize)
            {
                using var saved = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Put, path, Configuration(read, authorize), binding);
                Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "两类既有非呈现授权功能来源的generic管理入口必须有正式声明")]
    public async Task ActualIdentityAndRoleManagementSources_RejectMissingDeclarations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var manageAccount = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "identity.account.authorize");
        var manageRole = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.role.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Object311Manager", "对象授权主体",
            "object311-manager", "普通对象授权主体", manageAccount, manageRole);
        using var ordinary = actor.Client;
        var role = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles",
            new { Code = "OBJECT311", Name = "授权目标角色" });
        var account = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "object311-target", ENAME = "授权目标账号" });
        var observations = new List<(string Resource, HttpStatusCode Status)>();
        foreach (var (resource, id, feature) in new[]
        {
            ("authorization.role", role.GetProperty("id").GetString()!, manageRole),
            ("identity.account", account.GetProperty("account").GetProperty("eid").GetString()!, manageAccount)
        })
        {
            var path = "/api/authorization/data-scope/objects/" + resource + "/" + id;
            // 原API按真实authorize位置读取管理准备；没有另造只读查看入口。
            using var missing = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path + "/eligibility");
            observations.Add((resource, missing.StatusCode));
        }
        Assert.All(observations, observation => Assert.True(observation.Status == HttpStatusCode.BadRequest,
            observation.Resource + ": HTTP " + (int)observation.Status));
    }
}
