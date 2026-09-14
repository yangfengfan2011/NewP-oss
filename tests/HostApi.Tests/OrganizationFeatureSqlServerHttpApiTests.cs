using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationFeatureSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实声明创建及内部 lookup，不借用入口或其他操作")]
    public async Task PersonCreate_UsesDeclaredPositionAndCannotBorrowReadOrUpdate()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Organization("persons", "create", "list.pageActions");
        var (roleId, user) = await UserAsync(fixture, admin, "creator");
        using (user)
        {
            await http.GrantAsync(admin, roleId, create);
            using var created = await SendAsync(user, create, HttpMethod.Post, "/api/organization/persons", Person("CREATE"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
            using var wrongRead = await SendAsync(user, http.Organization("persons"), HttpMethod.Post, "/api/organization/persons", Person("READ"));
            Assert.Equal(HttpStatusCode.Forbidden, wrongRead.StatusCode);
            using var wrongUpdate = await SendAsync(user, create, HttpMethod.Put, "/api/organization/persons/" + id, new { ENAME = "不应更新" });
            Assert.Equal(HttpStatusCode.Forbidden, wrongUpdate.StatusCode);
            using var ungrantedUpdate = await SendAsync(user, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + id, new { ENAME = "未授权编辑" });
            Assert.Equal(HttpStatusCode.Forbidden, ungrantedUpdate.StatusCode);
            foreach (var path in new[] { "/api/organization/person-lookups/companies", "/api/organization/positions/options" })
            {
                using var lookup = await SendAsync(user, create, HttpMethod.Get, path);
                Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
            }
            using var unrelated = await SendAsync(user, create, HttpMethod.Get, "/api/organization/companies");
            Assert.Equal(HttpStatusCode.Forbidden, unrelated.StatusCode);
            var container = fixture.FeatureCatalog.Single(point => point.ModuleId == "Organization" && point.Key == "organization.structure" && point.Kind == "navigation");
            using var containerRead = await SendAsync(user, container, HttpMethod.Get, "/api/organization/persons");
            Assert.Equal(HttpStatusCode.Forbidden, containerRead.StatusCode);
            using var unknown = await SendAsync(user, create with { Position = "unknown" }, HttpMethod.Post, "/api/organization/persons", Person("UNKNOWN"));
            Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
            using var missing = await user.PostAsJsonAsync("/api/organization/persons", Person("MISSING"));
            Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "列表、树及详情同名编辑独立且原会话撤权立即生效")]
    public async Task EditPlacements_AreIndependentAndRevocationIsImmediate()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var id = await SeedAsync(http, admin, "EDIT");
        var edit = http.Organization("persons", "edit", "list.rowActions");
        var (roleId, user) = await UserAsync(fixture, admin, "editor");
        using (user)
        {
            await http.GrantAsync(admin, roleId, edit);
            using var success = await SendAsync(user, edit, HttpMethod.Put, "/api/organization/persons/" + id, new { ENAME = "已编辑" });
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            foreach (var other in new[] { http.Organization("structure", "edit", "contextMenu", "person"), http.Organization("persons", "edit", "detail.pageActions") })
            {
                using var wrong = await SendAsync(user, other, HttpMethod.Put, "/api/organization/persons/" + id, new { ENAME = "错位" });
                Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            }
            using var delete = await SendAsync(user, edit, HttpMethod.Delete, "/api/organization/persons/" + id);
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            await http.GrantAsync(admin, roleId, http.Organization("persons"));
            using var revoked = await SendAsync(user, edit, HttpMethod.Put, "/api/organization/persons/" + id, new { ENAME = "已撤销" });
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
            using var read = await SendAsync(user, http.Organization("persons"), HttpMethod.Get, "/api/organization/persons/" + id);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "启停位置与实际参数方向一致，旧混合入口不可绕行")]
    public async Task LifecyclePositions_RejectOppositePayloadAndLegacyRoute()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var id = await SeedAsync(http, admin, "STATUS");
        var disable = http.Organization("persons", "disable", "list.rowActions");
        var enable = http.Organization("persons", "enable", "list.rowActions");
        var (roleId, user) = await UserAsync(fixture, admin, "lifecycle");
        using (user)
        {
            await http.GrantAsync(admin, roleId, disable);
            using var opposite = await SendAsync(user, disable, HttpMethod.Put, $"/api/organization/persons/{id}/status/disable", new { isEnabled = true, reason = "反向参数" });
            Assert.Equal(HttpStatusCode.BadRequest, opposite.StatusCode);
            await AssertDirectionProblemAsync(opposite);
            foreach (var suffix in new[] { "/status/enable", "/status" })
            {
                using var denied = await SendAsync(user, disable, HttpMethod.Put, $"/api/organization/persons/{id}{suffix}", new { isEnabled = true, reason = "绕行" });
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using var success = await SendAsync(user, disable, HttpMethod.Put, $"/api/organization/persons/{id}/status/disable", new { isEnabled = false, reason = "正式停用" });
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            using var beforeGrant = await SendAsync(user, enable, HttpMethod.Put, $"/api/organization/persons/{id}/status/enable", new { isEnabled = true, reason = "未获授权" });
            Assert.Equal(HttpStatusCode.Forbidden, beforeGrant.StatusCode);
            await http.GrantAsync(admin, roleId, enable);
            using var restore = await SendAsync(user, enable, HttpMethod.Put, $"/api/organization/persons/{id}/status/enable", new { isEnabled = true, reason = "正式启用" });
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            using var wrongDirection = await SendAsync(user, enable, HttpMethod.Put, $"/api/organization/persons/{id}/status/enable", new { isEnabled = false, reason = "反向参数" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongDirection.StatusCode);
            await AssertDirectionProblemAsync(wrongDirection);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "导出及专用账号绑定读取独立于人员只读入口")]
    public async Task ExportAndBindingLookup_RequireTheirOwnPositions()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var id = await SeedAsync(http, admin, "EXPORT");
        var export = http.Organization("persons", "export", "list");
        var link = http.Organization("persons", "linkUser", "detail.custom");
        var (roleId, user) = await UserAsync(fixture, admin, "exporter");
        var account = await BusinessAuditSqlServerHttpApiTests.CreateAsync(admin, "/api/identity/accounts", new { ECODE = "binding-target", ENAME = "待绑定账号" });
        var binding = new { accountEid = account.GetProperty("account").GetProperty("eid").GetString(), expectedRevision = account.GetProperty("account").GetProperty("revision").GetInt64(), reason = "位置绑定" };
        using (user)
        {
            await http.GrantAsync(admin, roleId, http.Organization("persons"));
            var body = new { mode = "current", columns = new[] { new { key = "personNo", field = "PersonNo", title = "人员编号" } }, query = new { pageIndex = 1, pageSize = 20 } };
            using var denied = await SendAsync(user, export, HttpMethod.Post, "/api/organization/persons/export", body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedLink = await SendAsync(user, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{id}/user-binding/candidates");
            Assert.Equal(HttpStatusCode.Forbidden, deniedLink.StatusCode);
            using var deniedWrite = await SendAsync(user, http.Organization("persons"), HttpMethod.Put, $"/api/organization/persons/{id}/user-binding", binding);
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
            await http.GrantAsync(admin, roleId, export, link);
            using var exported = await SendAsync(user, export, HttpMethod.Post, "/api/organization/persons/export", body);
            Assert.True(exported.StatusCode == HttpStatusCode.OK, await exported.Content.ReadAsStringAsync());
            using var candidates = await SendAsync(user, link, HttpMethod.Get, $"/api/organization/persons/{id}/user-binding/candidates");
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
            using var bound = await SendAsync(user, link, HttpMethod.Put, $"/api/organization/persons/{id}/user-binding", binding);
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
        }
    }

    private static object Person(string code) => new { PersonNo = code, ENAME = code, EmploymentStatus = "FullTime", IsEnabled = true };
    private static async Task AssertDirectionProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REQUEST_VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty("isEnabled").EnumerateArray());
    }
    private static async Task<string> SeedAsync(FeatureHttpTestRequests http, HttpClient admin, string code)
    {
        using var response = await SendAsync(admin, http.Organization("persons", "create", "list.pageActions"), HttpMethod.Post, "/api/organization/persons", Person(code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return BusinessAuditSqlServerHttpApiTests.Id(await response.Content.ReadFromJsonAsync<JsonElement>());
    }
    private static async Task<(string RoleId, HttpClient Client)> UserAsync(BusinessAuditHostFixture fixture, HttpClient admin, string code)
    {
        var role = await BusinessAuditSqlServerHttpApiTests.CreateAsync(admin, "/api/authorization/roles", new { Code = code, Name = code });
        var account = await BusinessAuditSqlServerHttpApiTests.CreateAsync(admin, "/api/identity/accounts", new { ECODE = code, ENAME = code });
        using var membership = await admin.PutAsJsonAsync($"/api/authorization/users/{account.GetProperty("account").GetProperty("eid").GetString()}/roles", new { RoleCodes = new[] { code } });
        Assert.Equal(HttpStatusCode.NoContent, membership.StatusCode);
        return (role.GetProperty("id").GetString()!, await fixture.LoginAsync(code, account.GetProperty("temporaryCredential").GetString()!));
    }
}
