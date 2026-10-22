using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FunctionalCommandPreparationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "当前目录的账号、组织、永久绑定、角色和功能准备贯通到普通账号真实登录")]
    public async Task CurrentActorPreparation_UsesFormalButtonsAcrossAllOwners()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var person = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "Preparation326", "当前按钮准备角色", "preparation326", "当前按钮准备账号", person);
        using var ordinary = actor.Client;
        using var persons = await FeatureHttpTestRequests.SendAsync(ordinary, person, HttpMethod.Get,
            "/api/organization/persons?page=1&pageSize=20",
            dataAccess: http.Declaration("organization.person.list", person));
        Assert.True(persons.StatusCode == HttpStatusCode.OK, await persons.Content.ReadAsStringAsync());

        // 与Native准备相同：当前已初始化组织下创建未绑定账号和人员，再从正式按钮绑定并启停。
        var initialize = http.Organization("companies", "initialize", "list.pageActions");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post,
            "/api/organization/initialization", dataAccess: http.Declaration(initialize.BehaviorKey, initialize));
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var organization = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        using (var retry = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Post,
            "/api/organization/initialization", dataAccess: http.Declaration(initialize.BehaviorKey, initialize)))
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using (var eligibility = await FeatureHttpTestRequests.SendAsync(admin, initialize, HttpMethod.Get,
            "/api/organization/initialization/eligibility"))
            Assert.True((await eligibility.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowed").GetBoolean());
        var account = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "native-preparation326", ENAME = "正式功能账号准备" });
        var target = await http.CreateButtonAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new
            {
                PersonNo = "NATIVE326", ENAME = "正式功能人员准备", EmploymentStatus = "Active", IsEnabled = true,
                CompanyId = organization.GetProperty("companyId").GetString(),
                DepartmentId = organization.GetProperty("departmentId").GetString()
            });
        var id = target.GetProperty("fields").GetProperty("EID").GetString();
        var binding = http.Organization("persons", "linkUser", "detail.custom");
        using (var bound = await FeatureHttpTestRequests.SendAsync(admin, binding, HttpMethod.Put,
            $"/api/organization/persons/{id}/user-binding", new
            {
                AccountEid = account.GetProperty("account").GetProperty("eid").GetString(),
                ExpectedRevision = account.GetProperty("account").GetProperty("revision").GetInt64(), Reason = "正式用户路径准备"
            }, http.Declaration(binding.BehaviorKey, binding)))
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
        foreach (var enabled in new[] { false, true })
        {
            var command = enabled ? "enable" : "disable";
            var point = http.Organization("persons", command, enabled ? "list.rowActions" : "detail.pageActions");
            using var changed = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Put,
                $"/api/organization/persons/{id}/status/{command}", new { IsEnabled = enabled, Reason = "正式用户路径准备" },
                http.Declaration(point.BehaviorKey, point));
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        }
    }
}
