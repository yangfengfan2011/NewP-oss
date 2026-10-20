using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeLookupSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实人员表单候选、当前值及编辑共用最终读取范围")]
    public async Task PersonFormLookups_ApplyRulesAndObjectOverridesWithoutClearingHiddenRelations()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var readPerson = http.Organization("persons");
        var editPerson = http.Organization("persons", "edit", "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "LookupScope235", "候选范围", "lookup-scope-235", "候选范围人员",
            readPerson, editPerson);
        using var ordinary = actor.Client;

        var hiddenCompany = await CreateAsync("companies", new { ECODE = "C-235-H", ENAME = "A隐藏公司", SortOrder = "0100" });
        var visibleCompany = await CreateAsync("companies", new { ECODE = "C-235-V", ENAME = "B可读公司", SortOrder = "0200" });
        var hiddenDepartment = await CreateAsync("departments", new
        { CompanyId = hiddenCompany, ECODE = "D-235-H", ENAME = "A隐藏部门", SortOrder = "0100" });
        var visibleDepartment = await CreateAsync("departments", new
        { CompanyId = hiddenCompany, ECODE = "D-235-V", ENAME = "B可读部门", SortOrder = "0200" });
        var hiddenPosition = await CreateAsync("positions", new { ENAME = "A隐藏岗位" });
        var visiblePosition = await CreateAsync("positions", new { ENAME = "B可读岗位" });
        var person = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "LOOKUP-235", ENAME = "候选关系人员", CompanyId = hiddenCompany,
                DepartmentId = hiddenDepartment, PositionId = hiddenPosition, EmploymentStatus = "Active", IsEnabled = true }));

        await SaveRuleAsync("CompanyLookup235", "organization.company", "IsEnabled", true);
        await SaveRuleAsync("DepartmentLookup235", "organization.department", "EID", hiddenDepartment);
        await SaveRuleAsync("PositionLookup235", "organization.position", "EID", hiddenPosition);
        var companyManage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.company.authorize");
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, companyManage, HttpMethod.Put,
            $"/api/authorization/data-scope/objects/organization.company/{visibleCompany}",
            new DataScopeObjectDto("organization.company", visibleCompany,
                [new("VisibleCompany235", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow" })])))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertLookupAsync(readPerson,
            $"/api/organization/person-lookups/companies?pageSize=1&currentValueIds={hiddenCompany}",
            visibleCompany, hiddenCompany);
        await AssertLookupAsync(readPerson,
            $"/api/organization/person-lookups/departments?companyId={hiddenCompany}&pageSize=1&currentValueIds={hiddenDepartment}",
            visibleDepartment, hiddenDepartment);
        await AssertLookupAsync(readPerson,
            $"/api/organization/positions/options?pageSize=1&currentValueIds={hiddenPosition}",
            visiblePosition, hiddenPosition);

        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, editPerson, HttpMethod.Put,
            $"/api/organization/persons/{person}", new { ENAME = "已提交但保留关联",
                CompanyId = hiddenCompany, DepartmentId = hiddenDepartment, PositionId = hiddenPosition }))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await FeatureHttpTestRequests.ReadAsync(ordinary, readPerson,
            $"/api/organization/persons/{person}");
        var fields = detail.GetProperty("fields");
        Assert.Equal(hiddenCompany, fields.GetProperty("CompanyId").GetString());
        Assert.Equal(hiddenDepartment, fields.GetProperty("DepartmentId").GetString());
        Assert.Equal(hiddenPosition, fields.GetProperty("PositionId").GetString());

        await fixture.RestartWithActionAsync("read", false);
        using var readDisabled = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var login = await readDisabled.PostAsJsonAsync("/api/auth/login",
            new { ECODE = "lookup-scope-235", Password = "Audit-test-secure-938!" }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var response = await FeatureHttpTestRequests.SendAsync(readDisabled, readPerson, HttpMethod.Get,
            $"/api/organization/person-lookups/companies?pageSize=1&currentValueIds={hiddenCompany}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(hiddenCompany, Assert.Single(body.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("value").GetString());
            Assert.Equal("A隐藏公司", Assert.Single(body.RootElement.GetProperty("currentItems").EnumerateArray())
                .GetProperty("label").GetString());
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(readDisabled, readPerson, HttpMethod.Get,
            $"/api/organization/person-lookups/departments?companyId={hiddenCompany}&currentValueIds={hiddenDepartment}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("A隐藏部门", Assert.Single(body.RootElement.GetProperty("currentItems").EnumerateArray())
                .GetProperty("label").GetString());
        }

        async Task<string> CreateAsync(string tab, object payload) =>
            BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
                http.Organization(tab, "create", "list.pageActions"), $"/api/organization/{tab}", payload));

        async Task SaveRuleAsync<T>(string id, string resource, string field, T value)
        {
            var rule = new DataScopeRuleDto(id, id, null, id, true, resource,
                new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
                [new("Subject_" + id, "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"),
                HttpMethod.Put, "/api/authorization/data-scope/rules/" + id, rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        async Task AssertLookupAsync(ICP.Platform.Kernel.ModulePlanning.FeaturePoint point, string path,
            string allowedId, string hiddenId)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = body.RootElement.GetProperty("items");
            Assert.Equal(allowedId, Assert.Single(items.EnumerateArray()).GetProperty("value").GetString());
            var current = Assert.Single(body.RootElement.GetProperty("currentItems").EnumerateArray());
            Assert.Equal(hiddenId, current.GetProperty("value").GetString());
            Assert.Equal("***", current.GetProperty("label").GetString());
            Assert.Equal(3, current.EnumerateObject().Count());
        }
    }
}
