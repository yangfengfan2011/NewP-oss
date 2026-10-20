using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeOrganizationResourcesSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "同一动作开关跨三资源生效且不移除读取依赖或业务约束")]
    public async Task OrganizationResources_ActionSwitchIsSharedWithoutRemovingReadOrBusinessRules()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var features = new[] { "companies", "departments", "positions" }.SelectMany(tab => new[]
        {
            http.Organization(tab), http.Organization(tab, "create", "list.pageActions"),
            http.Organization(tab, "edit", "list.rowActions"),
            http.Organization(tab, "delete", "list.rowActions")
        }).ToArray();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "ScopeActionSwitch234", "组织动作开关", "scope-action-switch-234", "组织动作开关用户", features);
        actor.Client.Dispose();
        var company = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "SWITCH-234-C", ENAME = "开关公司", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "SWITCH-234-D", ENAME = "开关部门", SortOrder = "0100" }));
        var position = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("positions", "create", "list.pageActions"), "/api/organization/positions",
            new { ENAME = "开关岗位" }));
        var targets = new[] { (Tab: "companies", Resource: "organization.company", Id: company),
            (Tab: "departments", Resource: "organization.department", Id: department),
            (Tab: "positions", Resource: "organization.position", Id: position) };
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        foreach (var target in targets)
        {
            await SaveAsync("ReadEdit234_" + target.Tab, target.Resource, "EID", target.Id,
                new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny", ["delete"] = "deny" });
            await SaveAsync("Create234_" + target.Tab, target.Resource, "ENAME", "禁止创建",
                new Dictionary<string, string> { ["create"] = "deny" });
        }
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in targets)
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "create", "list.pageActions"), HttpMethod.Post,
                    "/api/organization/" + target.Tab, CreatePayload(target.Tab));
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{target.Tab}: {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
        await fixture.RestartWithActionAsync("create", false);
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in targets)
            {
                using (var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "create", "list.pageActions"), HttpMethod.Post,
                    "/api/organization/" + target.Tab, CreatePayload(target.Tab)))
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using (var response = await FeatureHttpTestRequests.SendAsync(ordinary,
                    http.Organization(target.Tab), HttpMethod.Get,
                    $"/api/organization/{target.Tab}/{target.Id}"))
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
            using (var response = await http.SendButtonAsync(ordinary,
                http.Organization("departments", "create", "list.pageActions"), HttpMethod.Post,
                "/api/organization/departments", new { ECODE = "INVALID-234", ENAME = "无公司部门", SortOrder = "0100" }))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await fixture.RestartWithActionAsync("edit", false);
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in targets)
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "edit", "list.rowActions"), HttpMethod.Put,
                    $"/api/organization/{target.Tab}/{target.Id}", new { ENAME = "仍有读取依赖" });
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool> { ["read"] = false, ["edit"] = false });
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in targets)
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "edit", "list.rowActions"), HttpMethod.Put,
                    $"/api/organization/{target.Tab}/{target.Id}", new { ENAME = "读取和编辑关闭-" + target.Tab });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        await fixture.RestartWithActionAsync("read", false);
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in targets)
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "edit", "list.rowActions"), HttpMethod.Put,
                    $"/api/organization/{target.Tab}/{target.Id}", new { ENAME = "编辑恢复仍拒绝" });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        await fixture.RestartWithActionAsync("delete", false);
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in new[] { targets[0], targets[2] })
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "delete", "list.rowActions"), HttpMethod.Delete,
                    $"/api/organization/{target.Tab}/{target.Id}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
        await fixture.RestartWithActionAsync("read", false);
        using (var ordinary = await LoginCurrentAsync())
        {
            foreach (var target in new[] { targets[0], targets[2] })
            {
                using var response = await http.SendButtonAsync(ordinary,
                    http.Organization(target.Tab, "delete", "list.rowActions"), HttpMethod.Delete,
                    $"/api/organization/{target.Tab}/{target.Id}");
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool> { ["read"] = false, ["delete"] = false });
        using (var ordinary = await LoginCurrentAsync())
        {
            using (var response = await http.SendButtonAsync(ordinary,
                http.Organization("companies", "delete", "list.rowActions"), HttpMethod.Delete,
                "/api/organization/companies/" + company))
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using (var response = await http.SendButtonAsync(ordinary,
                http.Organization("positions", "delete", "list.rowActions"), HttpMethod.Delete,
                "/api/organization/positions/" + position))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        object CreatePayload(string tab) => tab switch
        {
            "companies" => new { ECODE = "SWITCH-234-NEW", ENAME = "禁止创建", SortOrder = "0200" },
            "departments" => new { CompanyId = company, ECODE = "SWITCH-234-NEW", ENAME = "禁止创建", SortOrder = "0200" },
            _ => new { ENAME = "禁止创建" }
        };

        async Task SaveAsync(string id, string resource, string field, string value, Dictionary<string, string> actions)
        {
            var rule = new DataScopeRuleDto(id, id, null, id, true, resource,
                new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
                [new("Subject_" + id, "all", null, 0, actions)], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + id, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        async Task<HttpClient> LoginCurrentAsync()
        {
            var client = fixture.CreateClient();
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login",
                new { ECODE = "scope-action-switch-234", Password = "Audit-test-secure-938!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            return client;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "三类组织资源正式 CRUD、规则与对象管理使用同一资源身份")]
    public async Task OrganizationResources_OrdinaryCrudAndObjectManagementUseFinalScope()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var features = new[] { "companies", "departments", "positions" }.SelectMany(tab => new[]
            {
                http.Organization(tab),
                http.Organization(tab, "create", "list.pageActions"),
                http.Organization(tab, "edit", "list.rowActions"),
                http.Organization(tab, "delete", "list.rowActions"),
                http.Organization(tab, "authorize", "list.rowActions")
            }).Append(http.Organization("persons")).ToArray();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "ScopeOrganization", "组织资源范围", "scope-organization", "组织资源用户", features);
        using var ordinary = actor.Client;
        var company = await CreateAsync("companies", new { ECODE = "SCOPE-C", ENAME = "范围公司", SortOrder = "0100" });
        var department = await CreateAsync("departments", new { CompanyId = company, ECODE = "SCOPE-D", ENAME = "范围部门", SortOrder = "0100" });
        var position = await CreateAsync("positions", new { ENAME = "范围岗位", Description = "资源授权" });
        var person = await http.CreateButtonAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SCOPE-FOUR", ENAME = "四资源对照", EmploymentStatus = "Active", IsEnabled = true });
        var targets = new[]
        {
            (Tab: "companies", Resource: "organization.company", Id: company, Name: "范围公司"),
            (Tab: "departments", Resource: "organization.department", Id: department, Name: "范围部门"),
            (Tab: "positions", Resource: "organization.position", Id: position, Name: "范围岗位")
        };
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("persons"),
            HttpMethod.Get, "/api/organization/persons/" + BusinessAuditSqlServerHttpApiTests.Id(person)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var target in targets)
        {
            var read = http.Organization(target.Tab);
            var edit = http.Organization(target.Tab, "edit", "list.rowActions");
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
                $"/api/organization/{target.Tab}/{target.Id}"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await http.SendButtonAsync(ordinary, edit, HttpMethod.Put,
                $"/api/organization/{target.Tab}/{target.Id}", new { ENAME = target.Name + "更新" }))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var disposableDepartment = await CreateAsync("departments", new
        { CompanyId = company, ECODE = "SCOPE-DELETE-D", ENAME = "待删部门", SortOrder = "0200" });
        var disposableCompany = await CreateAsync("companies", new
        { ECODE = "SCOPE-DELETE-C", ENAME = "待删公司", SortOrder = "0200" });
        var disposablePosition = await CreateAsync("positions", new { ENAME = "待删岗位" });
        foreach (var target in new[] { (Tab: "departments", Id: disposableDepartment),
                     (Tab: "companies", Id: disposableCompany), (Tab: "positions", Id: disposablePosition) })
        {
            using var response = await http.SendButtonAsync(ordinary,
                http.Organization(target.Tab, "delete", "list.rowActions"), HttpMethod.Delete,
                $"/api/organization/{target.Tab}/{target.Id}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using (var response = await http.SendButtonAsync(ordinary,
            http.Organization("companies", "delete", "list.rowActions"), HttpMethod.Delete,
            "/api/organization/companies/" + company))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var ruleSave = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var incompatible = new DataScopeRuleDto("Scope234InvalidCompanyField", "Scope234InvalidCompanyField",
            null, "公司不可用部门字段", true, "organization.company",
            new("attribute", "CompanyId", "equal", JsonSerializer.SerializeToElement(company)), [], []);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope234InvalidCompanyField", incompatible))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope234DepartmentCompanyField",
            incompatible with { Id = "Scope234DepartmentCompanyField", RootId = "Scope234DepartmentCompanyField",
                ResourceKey = "organization.department" }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var target in targets)
        {
            var read = http.Organization(target.Tab);
            var manage = http.Organization(target.Tab, "authorize", "list.rowActions");
            var path = $"/api/organization/{target.Tab}/{target.Id}";
            var objectPath = $"/api/authorization/data-scope/objects/{target.Resource}/{target.Id}";
            var ruleId = "Scope234_" + target.Tab;
            var beforeList = await FeatureHttpTestRequests.ReadAsync(ordinary, read,
                $"/api/organization/{target.Tab}?page=1&pageSize=1");
            var rule = new DataScopeRuleDto(ruleId, ruleId, null, "三资源读取拒绝", true,
                target.Resource, new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target.Id)),
                [new("Subject234_" + target.Tab, "all", null, 0,
                    new Dictionary<string, string> { ["read"] = "deny" })], []);
            using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + ruleId, rule))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var afterList = await FeatureHttpTestRequests.ReadAsync(ordinary, read,
                $"/api/organization/{target.Tab}?page=1&pageSize=1");
            Assert.Equal(beforeList.GetProperty("total").GetInt32() - 1, afterList.GetProperty("total").GetInt32());
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
                objectPath + "/eligibility"))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var grant = new DataScopeObjectDto(target.Resource, target.Id,
                [new("Object234_" + target.Tab, "user", actor.AccountId, 0,
                    new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]);
            using (var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, objectPath, grant))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            if (target.Tab == "companies")
            {
                using var scope = fixture.Services.CreateScope();
                var grants = scope.ServiceProvider.GetRequiredService<DataScopeObjectGrants>();
                Assert.Single((await grants.GetAsync("organization.company", target.Id, CancellationToken.None)).Subjects);
                Assert.Empty((await grants.GetAsync("organization.department", target.Id, CancellationToken.None)).Subjects);
            }
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get,
                objectPath + "/eligibility"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, objectPath))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (target.Tab == "departments")
            {
                var auditPath = $"/api/audit/business-changes?TargetId={target.Resource}%2F{target.Id}&EventType=dataScope.object.changed";
                var auditBefore = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), auditPath);
                foreach (var mode in new[] { "register", "save", "commit" })
                {
                    fixture.Fault.Mode = mode;
                    try
                    {
                        using var failed = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                            objectPath, grant with { Subjects = [grant.Subjects[0] with
                            { Actions = new Dictionary<string, string> { ["read"] = "deny" } }] });
                        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                    }
                    finally { fixture.Fault.Mode = ""; }
                    using var kept = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, path);
                    Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
                }
                var auditAfter = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), auditPath);
                Assert.Equal(auditBefore.GetProperty("items").GetArrayLength(), auditAfter.GetProperty("items").GetArrayLength());
            }
            var relinquish = grant with { Subjects = [grant.Subjects[0] with
            { Actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny" } }] };
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put,
                objectPath, relinquish))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, objectPath))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
                $"/api/audit/business-changes?TargetId={target.Resource}%2F{target.Id}&EventType=dataScope.object.changed");
            Assert.NotEmpty(audit.GetProperty("items").EnumerateArray());
        }

        async Task<string> CreateAsync(string tab, object payload) => BusinessAuditSqlServerHttpApiTests.Id(
            await http.CreateButtonAsync(ordinary, http.Organization(tab, "create", "list.pageActions"),
                "/api/organization/" + tab, payload));
    }
}
