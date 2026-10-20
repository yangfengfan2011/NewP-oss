using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationFeatureOnlyExecutionTests
{
    [Fact]
    public void ManagementMethods_KeepTheirSpecificFeatureAndAdministratorClassification()
    {
        foreach (var (owner, method, behavior, administrator) in new[]
        {
            (typeof(PersonLifecycleController), "Enable", "organization.person.enable", false),
            (typeof(PersonLifecycleController), "Disable", "organization.person.disable", false),
            (typeof(PersonUserBindingsController), "SearchCandidates", "organization.person.linkUser.lookup", false),
            (typeof(PersonUserBindingsController), "Bind", "organization.person.linkUser", false),
            (typeof(OrganizationInitializationController), "Eligibility", "organization.company.initialize", false),
            (typeof(OrganizationInitializationController), "Initialize", "organization.company.initialize", true)
        })
        {
            var endpoint = owner.GetMethod(method)!;
            Assert.Equal(behavior, Assert.Single(endpoint.GetCustomAttributes<RequireFeatureAttribute>()).BehaviorKey);
            Assert.Equal(administrator, endpoint.GetCustomAttributes<AuthorizeAttribute>().Any(item => item.Policy == "AdminOnly"));
            Assert.Empty(endpoint.GetCustomAttributes<StandardCrudDataExecutionAttribute>());
            Assert.Empty(endpoint.GetCustomAttributes<OrganizationLookupExecutionAttribute>());
        }
        Assert.Single(typeof(PersonLifecycleController).GetMethod("SetStatus")!.GetCustomAttributes<DenyAllAttribute>());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员管理沿原功能资格执行，业务read拒绝不新增管理预检，缺功能仍拒绝")]
    public async Task PersonManagement_PreservesFeatureOnlyQualificationWhenObjectReadIsDenied()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var actor = await HttpTestActors.CreateAuthenticatedActorAsync(fixture, admin, "Feature310", "人员管理", "feature310", "管理主体");
        using var ordinary = actor.Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/organization/persons",
            new { PersonNo = "FEATURE310", ENAME = "不可读管理目标", EmploymentStatus = "FullTime", IsEnabled = true }, Binding("organization.person.create", create));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var person = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        var account = (await FeatureHttpTestRequests.CreateAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "feature310-target", ENAME = "待绑定账号" }, http.Declaration("identity.account.create", http.Identity("create")))).GetProperty("account");
        var manage = http.Organization("persons", "authorize", "detail.pageActions");
        using (var configured = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/organization.person/" + person,
            new DataScopeObjectDto("organization.person", person,
                [new("Feature310Read", "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = "deny" })]),
            Binding("organization.person.authorize", manage)))
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        var link = http.Organization("persons", "linkUser", "detail.custom");
        var disable = http.Organization("persons", "disable", "list.rowActions");
        var enable = http.Organization("persons", "enable", "list.rowActions");
        var prefix = "/api/organization/persons/" + person;
        var request = new { accountEid = account.GetProperty("eid").GetString(), expectedRevision = account.GetProperty("revision").GetInt64(), reason = "正式功能管理" };
        using (var noFeature = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, disable, HttpMethod.Put, prefix + "/status/disable", new { isEnabled = false, reason = "无功能" }))
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        using (var noFeature = await FeatureHttpTestRequests.SendAsync(ordinary, link, HttpMethod.Get, prefix + "/user-binding/candidates"))
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        using (var noFeature = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, link, HttpMethod.Put, prefix + "/user-binding", request))
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        await http.GrantAsync(admin, actor.RoleId, link, disable, enable);
        var before = await FeatureHttpTestRequests.ReadTextAsync(admin, http.Organization("persons"), prefix,
            Binding("organization.person.read", http.Organization("persons")));
        using (var missingBinding = await FeatureHttpTestRequests.SendAsync(ordinary, link, HttpMethod.Put, prefix + "/user-binding", request))
            Assert.Equal(HttpStatusCode.BadRequest, missingBinding.StatusCode);
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, disable, HttpMethod.Put, prefix + "/status/disable",
            new { isEnabled = false, reason = "借用绑定按钮" }, Binding("organization.person.linkUser", link)))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, disable, HttpMethod.Put, prefix + "/status/disable", new { isEnabled = false, reason = "缺少按钮身份" }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(before, await FeatureHttpTestRequests.ReadTextAsync(admin, http.Organization("persons"), prefix,
            Binding("organization.person.read", http.Organization("persons"))));
        var read = http.Organization("persons");
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, prefix, dataAccess: Binding("organization.person.read", read)))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        // 候选账号来自原Identity管理目录，不能被业务对象read规则截断。
        using (var candidates = await FeatureHttpTestRequests.SendAsync(ordinary, link, HttpMethod.Get, prefix + "/user-binding/candidates?query=feature310-target"))
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
        using (var bound = await FeatureHttpTestRequests.SendAsync(ordinary, link, HttpMethod.Put, prefix + "/user-binding", request, Binding("organization.person.linkUser", link)))
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
        foreach (var (feature, enabled) in new[] { (disable, false), (enable, true) })
        {
            using var changed = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Put,
                prefix + "/status/" + (enabled ? "enable" : "disable"), new { isEnabled = enabled, reason = "无需对象read的原管理命令" }, Binding("organization.person." + (enabled ? "enable" : "disable"), feature));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }

        await http.GrantAsync(admin, actor.RoleId, link, disable);
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, enable, HttpMethod.Put, prefix + "/status/enable",
            new { isEnabled = true, reason = "撤销后提交" }, Binding("organization.person.enable", enable)))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);

        ActionDataAccessRequirement? Binding(string execution, FeaturePoint feature) => plan.ActionDataAccessCatalog.SingleOrDefault(item =>
            (item.Execution == execution || item.BusinessBinding == execution) && item.FeaturePositions.Contains(feature.Position));
    }
}
