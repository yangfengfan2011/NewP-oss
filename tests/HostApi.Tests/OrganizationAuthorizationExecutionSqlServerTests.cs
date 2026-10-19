using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationAuthorizationExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四类对象通用授权消费真实owner位置与正式声明，完整保存不依赖点击预检或缩减可委派动作")]
    public async Task FourResourceManagement_UsesItsBindingAndKeepsReadAuthorizeAndDelegation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var tabs = new[] { (Tab: "companies", Resource: "organization.company"), (Tab: "departments", Resource: "organization.department"),
            (Tab: "positions", Resource: "organization.position"), (Tab: "persons", Resource: "organization.person") };
        var features = tabs.SelectMany(item => new[] { Manage(item.Tab), http.Organization(item.Tab, "delete", "list.rowActions") }).ToArray();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Authorization310", "组织授权",
            "authorization310", "组织授权主体", features);
        using var ordinary = actor.Client;
        var company = await CreateAsync("companies", "organization.company", new { ECODE = "AUTH310-C", ENAME = "授权公司", SortOrder = "0100" });
        var department = await CreateAsync("departments", "organization.department", new { CompanyId = company, ECODE = "AUTH310-D", ENAME = "授权部门", SortOrder = "0100" });
        var position = await CreateAsync("positions", "organization.position", new { ENAME = "授权岗位" });
        var person = await CreateAsync("persons", "organization.person", new { PersonNo = "AUTH310-P", ENAME = "授权人员", EmploymentStatus = "FullTime", IsEnabled = true });
        var ids = new[] { company, department, position, person };
        for (var index = 0; index < tabs.Length; index++)
        {
            var target = tabs[index]; var id = ids[index]; var feature = Manage(target.Tab);
            var endpoint = "/api/authorization/data-scope/objects/" + target.Resource + "/" + id;
            var initial = Configuration("allow", "allow", false);
            await SaveAsync(admin, initial);
            using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, endpoint + "/eligibility"))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            var binding = Binding(target.Resource, feature);
            Assert.NotNull(binding);
            // 第一次合法调用直接保存，不以此前的点击预检作为正式写入许可。
            using (var save = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Put, endpoint,
                Configuration("allow", "allow", true), binding))
                Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
            using (var get = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, endpoint, dataAccess: binding))
            {
                Assert.Equal(HttpStatusCode.OK, get.StatusCode);
                using var body = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
                Assert.Equal(2, body.RootElement.GetProperty("subjects").GetArrayLength());
            }
            using (var deleted = await FeatureHttpTestRequests.SendAsync(ordinary,
                http.Organization(target.Tab, "delete", "list.rowActions"), HttpMethod.Delete, "/api/organization/" + target.Tab + "/" + id,
                dataAccess: plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == target.Resource + ".delete"
                    && item.FeaturePositions.Contains(http.Organization(target.Tab, "delete", "list.rowActions").Position))))
                Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
            var borrowed = plan.ActionDataAccessCatalog.Single(item => item.Execution == target.Resource + ".list"
                && item.FeaturePositions.Contains(http.Organization(target.Tab).Position));
            using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, endpoint, dataAccess: borrowed))
                Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            await SaveAsync(admin, Configuration("deny", "allow", false));
            using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, endpoint, dataAccess: binding))
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            await SaveAsync(admin, Configuration("allow", "deny", false));
            using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, endpoint, dataAccess: binding))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            DataScopeObjectDto Configuration(string read, string authorize, bool delegateDelete) => new(target.Resource, id,
                delegateDelete
                    ? [new("Self310-" + target.Tab, "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = read, ["authorize"] = authorize, ["delete"] = "deny" }),
                        new("All310-" + target.Tab, "all", null, 0, new Dictionary<string, string> { ["delete"] = "allow" })]
                    : [new("Self310-" + target.Tab, "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = read, ["authorize"] = authorize, ["delete"] = "deny" })]);
            async Task SaveAsync(HttpClient client, DataScopeObjectDto body)
            {
                using var saved = await FeatureHttpTestRequests.SendAsync(client, feature, HttpMethod.Put, endpoint, body, Binding(target.Resource, feature));
                Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            }
        }

        FeaturePoint Manage(string tab) => http.Organization(tab, "authorize", tab == "persons" ? "detail.pageActions" : "list.rowActions");
        ActionDataAccessRequirement? Binding(string resource, FeaturePoint feature) => plan.ActionDataAccessCatalog.SingleOrDefault(item =>
            item.Execution == resource + ".authorization.manage" && item.FeaturePositions.Contains(feature.Position));
        async Task<string> CreateAsync(string tab, string resource, object body)
        {
            var feature = http.Organization(tab, "create", "list.pageActions");
            using var created = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, "/api/organization/" + tab, body,
                plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == resource + ".create" && item.FeaturePositions.Contains(feature.Position)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return BusinessAuditSqlServerHttpApiTests.Id(JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync()));
        }
    }
}
