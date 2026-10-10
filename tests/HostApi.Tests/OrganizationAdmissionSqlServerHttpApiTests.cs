using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "普通账号首次登录必须具有真实有效组织")]
    public async Task Login_UnboundTemporaryAccountCannotCreateSession()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var created = await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "unbound", ENAME = "未绑定" });
        using var response = await admin.PostAsJsonAsync("/api/auth/login", new { ECODE = "unbound", Password = created.GetProperty("temporaryCredential").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "清空再补齐不复活旧会话，正常调动保留会话")]
    public async Task AffiliationInterruption_RevokesAllSessionsWithoutAnInterveningRequest()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        using var first = await fixture.LoginAsync("admitted", prepared.Password);
        using var second = fixture.CreateClient();
        await LoginAsync(second, "Audit-test-secure-938!");
        var next = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "NEXT", ENAME = "新部门", CompanyId = prepared.Company, SortOrder = "02" }));
        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = prepared.Company, DepartmentId = next });
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/current-user")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/current-user")).StatusCode);
        var view = await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync();
        Assert.Equal(next, view.FindByAccount(prepared.Account)!.Department!["EID"]);

        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = (string?)null, DepartmentId = (string?)null });
        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = prepared.Company, DepartmentId = next });
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/current-user")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/current-user")).StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var sessions = await db.Set<Dictionary<string, object?>>("AuthenticationSession").AsNoTracking()
            .Where(row => EF.Property<string>(row, "AccountEID") == prepared.Account).ToListAsync();
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, row => Assert.NotNull(row["RevokedAt"]));
        using var fresh = fixture.CreateClient();
        await LoginAsync(fresh, "Audit-test-secure-938!");
    }

    private static async Task<(string Person, string Company, string Department, string Account, string Password)> PrepareAsync(HttpClient admin, FeatureHttpTestRequests http)
    {
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "ADMIT", ENAME = "准入公司", SortOrder = "01" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "ADMIT", ENAME = "准入部门", CompanyId = company, SortOrder = "01" }));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "ADMIT", ENAME = "准入人员", CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var created = await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "admitted", ENAME = "准入账号" });
        var account = created.GetProperty("account");
        using var bound = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = account.GetProperty("eid").GetString(), expectedRevision = account.GetProperty("revision").GetInt64(), reason = "准入准备" });
        Assert.True(bound.IsSuccessStatusCode, await bound.Content.ReadAsStringAsync());
        return (person, company, department, account.GetProperty("eid").GetString()!, created.GetProperty("temporaryCredential").GetString()!);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "叶级部门停用再恢复不会复活多会话，也不修改下级自身状态")]
    public async Task LeafDisableRestore_WithoutRequestsPermanentlyRevokesSessions()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        using var first = await fixture.LoginAsync("admitted", prepared.Password);
        using var second = fixture.CreateClient();
        await LoginAsync(second, "Audit-test-secure-938!");
        foreach (var enabled in new[] { false, true })
        {
            using var response = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/departments/{prepared.Department}", new { IsEnabled = enabled });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/current-user")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/current-user")).StatusCode);
        var current = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(prepared.Account)!;
        Assert.Equal(true, current.Person["IsEnabled"]);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Equal(IdentityAccountStatus.Enabled, (await scope.ServiceProvider.GetRequiredService<IIdentityAccountDirectory>().FindAsync(prepared.Account))!.Status);
        using var fresh = fixture.CreateClient();
        await LoginAsync(fresh, "Audit-test-secure-938!");
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "缓存故障保留Cookie和会话，LocalAdmin保持维护身份")]
    public async Task CacheUnavailable_PreservesCookieAndDoesNotRevokeOrBlockAdministrator()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddSingleton<OrganizationCache, RecoveryCache>());
        using var admin = await fixture.LoginAdministratorAsync();
        var prepared = await PrepareAsync(admin, new FeatureHttpTestRequests(fixture));
        using var client = await fixture.LoginAsync("admitted", prepared.Password);
        var accountBeforeFault = await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var cache = (RecoveryCache)fixture.Services.GetRequiredService<OrganizationCache>();
        await using (var scope = await cache.BeginAsync(default)) { await scope.PrepareAsync([], default); scope.MarkUnavailable(); }
        using var unavailable = await client.GetAsync("/api/auth/current-user");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        using var organizationRead = await SendAsync(admin, new FeatureHttpTestRequests(fixture).Organization("companies"), HttpMethod.Get, "/api/organization/companies");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, organizationRead.StatusCode);
        using var administrator = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(administrator);
        using var maintenance = await administrator.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, maintenance.StatusCode);
        using var login = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(login);
        using var blocked = await login.PostAsJsonAsync("/api/auth/login", new { ECODE = "admitted", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal("Persistence.CacheUnavailable", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var statusChange = await SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("disable"), HttpMethod.Put,
            $"/api/identity/accounts/{prepared.Account}/status/disable",
            new { TargetStatus = "Disabled", ExpectedRevision = accountBeforeFault.GetProperty("revision").GetInt64(), Reason = "缓存故障期间的协调写入" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statusChange.StatusCode);
        Assert.Equal("Persistence.CacheUnavailable", (await statusChange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // 可控范围读取释放后，正式后台恢复保留同一Cookie。
        cache.Release.TrySetResult();
        await cache.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/current-user")).StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "准入owner缺失不得成为普通账号旁路，LocalAdmin独立可用")]
    public async Task MissingAdmissionOwner_FailsClosedForOrdinaryLogin()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.RemoveAll<IIdentityAdmissionPolicy>());
        using var admin = await fixture.LoginAdministratorAsync();
        var prepared = await PrepareAsync(admin, new FeatureHttpTestRequests(fixture));
        using var client = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = "admitted", Password = prepared.Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("AUTH_ADMISSION_UNAVAILABLE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private static async Task LoginAsync(HttpClient client, string password)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = "admitted", Password = password });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task UpdateAsync(HttpClient admin, FeatureHttpTestRequests http, string person, object values)
    {
        using var response = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, $"/api/organization/persons/{person}", values);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
