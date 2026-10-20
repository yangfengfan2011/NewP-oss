using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

internal sealed record HttpTestActor(string AccountId, string RoleId, HttpClient Client);

// 两个入口固定授权相对登录的时序；临时凭据只在内部准备结果中短暂传递。
internal static class HttpTestActors
{
    // 为登录前已具备明确功能集合的普通主体创建独立 Cookie client。
    internal static async Task<HttpTestActor> CreateAuthorizedActorAsync(
        BusinessAuditHostFixture fixture,
        HttpClient administrator,
        string roleCode,
        string roleName,
        string accountCode,
        string accountName,
        params FeaturePoint[] features)
    {
        var prepared = await PrepareAsync(
            fixture,
            administrator,
            roleCode,
            roleName,
            accountCode,
            accountName);
        await new FeatureHttpTestRequests(fixture).GrantAsync(administrator, prepared.RoleId, features);
        return await LoginAsync(fixture, prepared);
    }

    // 创建未预置功能的普通主体，供调用方用同一 Cookie 证明后续授权即时生效。
    internal static async Task<HttpTestActor> CreateAuthenticatedActorAsync(
        BusinessAuditHostFixture fixture,
        HttpClient administrator,
        string roleCode,
        string roleName,
        string accountCode,
        string accountName)
    {
        var prepared = await PrepareAsync(
            fixture,
            administrator,
            roleCode,
            roleName,
            accountCode,
            accountName);
        return await LoginAsync(fixture, prepared);
    }

    private static async Task<PreparedActor> PrepareAsync(
        BusinessAuditHostFixture fixture,
        HttpClient administrator,
        string roleCode,
        string roleName,
        string accountCode,
        string accountName)
    {
        var http = new FeatureHttpTestRequests(fixture);
        var role = await FeatureHttpTestRequests.CreateAsync(
            administrator,
            http.Role("create"),
            "/api/authorization/roles",
            new { Code = roleCode, Name = roleName }, http.Declaration("authorization.role.create", http.Role("create")));
        var account = await FeatureHttpTestRequests.CreateAsync(
            administrator,
            http.Identity("create"),
            "/api/identity/accounts",
            new { ECODE = accountCode, ENAME = accountName }, http.Declaration("identity.account.create", http.Identity("create")));
        var accountId = account.GetProperty("account").GetProperty("eid").GetString()!;
        await PrepareOrganizationAsync(administrator, http, account.GetProperty("account"));
        using var assignment = await FeatureHttpTestRequests.SendAsync(
            administrator,
            http.Role("userRoles"),
            HttpMethod.Put,
            $"/api/authorization/users/{accountId}/roles",
            new { RoleCodes = new[] { roleCode } }, http.Declaration("authorization.role.userRoles", http.Role("userRoles")));
        Assert.True(
            assignment.StatusCode == HttpStatusCode.NoContent,
            await assignment.Content.ReadAsStringAsync());
        return new(
            accountId,
            role.GetProperty("id").GetString()!,
            accountCode,
            account.GetProperty("temporaryCredential").GetString()!);
    }

    private static async Task<HttpTestActor> LoginAsync(
        BusinessAuditHostFixture fixture,
        PreparedActor prepared)
    {
        var client = await fixture.LoginAsync(prepared.AccountCode, prepared.TemporaryCredential);
        return new(prepared.AccountId, prepared.RoleId, client);
    }

    private sealed record PreparedActor(
        string AccountId,
        string RoleId,
        string AccountCode,
        string TemporaryCredential);

    // 普通主体的业务身份通过正式API准备，角色是否授权仍由各用例单独决定。
    internal static async Task PrepareOrganizationAsync(HttpClient administrator, FeatureHttpTestRequests http, JsonElement account)
    {
        var accountId = account.GetProperty("eid").GetString()!;
        var suffix = accountId[^12..];
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(administrator,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "ACT-" + suffix, ENAME = "测试准入公司-" + suffix, SortOrder = "9999" },
            http.Declaration("organization.company.create", http.Organization("companies", "create", "list.pageActions"))));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(administrator,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { ECODE = "ACT-" + suffix, ENAME = "测试准入部门-" + suffix, CompanyId = company, SortOrder = "9999" },
            http.Declaration("organization.department.create", http.Organization("departments", "create", "list.pageActions"))));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(administrator,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "ZZZ-ACTOR-" + suffix, ENAME = "测试准入人员-" + suffix, CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true },
            http.Declaration("organization.person.create", http.Organization("persons", "create", "list.pageActions"))));
        using var binding = await FeatureHttpTestRequests.SendAsync(administrator, http.Organization("persons", "linkUser", "detail.custom"),
            HttpMethod.Put, $"/api/organization/persons/{person}/user-binding",
            new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "准备普通测试主体" }, http.Declaration("organization.person.linkUser", http.Organization("persons", "linkUser", "detail.custom")));
        Assert.True(binding.IsSuccessStatusCode, await binding.Content.ReadAsStringAsync());
    }
}
