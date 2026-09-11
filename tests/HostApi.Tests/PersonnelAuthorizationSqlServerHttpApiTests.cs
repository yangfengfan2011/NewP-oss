using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.BusinessAuditSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class PersonnelAuthorizationSqlServerHttpApiTests
{
    private static readonly string[] Views = ["Organization.Company.View", "Organization.Department.View", "Organization.Position.View", "Organization.Person.View"];
    private static readonly string[] Maintenance = [.. Views, "Organization.Person.Create", "Organization.Person.Update"];

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实三账号完整矩阵及原Cookie撤销全部权限来源")]
    public async Task RealSessions_EnforceMatrixAndRevokeAllUpdateSourcesWithoutRelogin()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var company = Id(await CreateAsync(admin, "/api/organization/companies", new { ECODE = "MATRIX", ENAME = "公司", SortOrder = "0100" }));
        var a = Id(await CreateAsync(admin, "/api/organization/departments", new { ECODE = "A", ENAME = "部门A", CompanyId = company, SortOrder = "0100" }));
        var b = Id(await CreateAsync(admin, "/api/organization/departments", new { ECODE = "B", ENAME = "部门B", CompanyId = company, SortOrder = "0200" }));
        var position = Id(await CreateAsync(admin, "/api/organization/positions", new { ECODE = "P", ENAME = "岗位", SortOrder = "0100" }));
        var maintainer = await CreateActorAsync(fixture, admin, "matrix-m", Maintenance);
        var reader = await CreateActorAsync(fixture, admin, "matrix-r", Views);
        using var writer = maintainer.Client;
        using var read = reader.Client;
        var person = Id(await CreateAsync(writer, "/api/organization/persons", Person("MATRIX-1", company, a, position)));
        foreach (var client in new[] { writer, read })
        {
            foreach (var (route, id) in new[] { ("companies", company), ("departments", a), ("positions", position), ("persons", person) })
            {
                await AssertStatusAsync(client.GetAsync($"/api/organization/{route}"), HttpStatusCode.OK);
                await AssertStatusAsync(client.GetAsync($"/api/organization/{route}/{id}"), HttpStatusCode.OK);
                await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
                await AssertStatusAsync(client.PostAsJsonAsync($"/api/organization/{route}/export", new { Mode = "current", Columns = new[] { new { Field = "ENAME", Title = "名称" } } }), HttpStatusCode.OK);
            }
            foreach (var route in new[] {
                "/api/trees/organization.structure/roots?scope=global",
                $"/api/trees/organization.structure/children?scope=global&nodeKey=department:{a}",
                $"/api/trees/organization.structure/path?scope=global&nodeKey=person:{person}",
                "/api/trees/organization.structure/search?scope=global&keyword=MATRIX&page=1&pageSize=20",
                "/api/organization/person-lookups/companies?page=1&pageSize=1",
                $"/api/organization/person-lookups/departments?companyId={company}&page=1&pageSize=1",
                "/api/organization/positions/options",
                "/api/organization/department-lookups/companies",
                $"/api/organization/department-lookups/parents?companyId={company}" })
                await AssertStatusAsync(client.GetAsync(route), HttpStatusCode.OK);
            foreach (var route in new[] { "/api/identity/accounts", "/api/authorization/roles", "/api/authorization/permissions", "/api/audit/business-changes" })
                await AssertStatusAsync(client.GetAsync(route), HttpStatusCode.Forbidden);
            await AssertStatusAsync(client.DeleteAsync($"/api/organization/persons/{person}"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(client.PutAsJsonAsync($"/api/organization/persons/{person}/user-binding", new { UserId = reader.Id, Reason = "不应绑定" }), HttpStatusCode.Forbidden);
            foreach (var (route, id) in new[] { ("companies", company), ("departments", a), ("positions", position) })
            {
                await AssertStatusAsync(client.PostAsJsonAsync($"/api/organization/{route}", new { ECODE = "DENIED", ENAME = "禁止" }), HttpStatusCode.Forbidden);
                await AssertStatusAsync(client.PutAsJsonAsync($"/api/organization/{route}/{id}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
                await AssertStatusAsync(client.DeleteAsync($"/api/organization/{route}/{id}"), HttpStatusCode.Forbidden);
            }
            await AssertStatusAsync(client.PostAsJsonAsync("/api/identity/accounts", new { ECODE = "denied" }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(client.PutAsJsonAsync($"/api/authorization/roles/{maintainer.RoleId}/permissions", new { PermissionCodes = Views }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(client.PutAsJsonAsync($"/api/authorization/users/{maintainer.Id}/roles", new { RoleCodes = Array.Empty<string>() }), HttpStatusCode.Forbidden);
        }
        var beforeRejected = await HistoryAsync(admin, person);
        await AssertStatusAsync(read.PostAsync("/api/organization/persons", JsonContent.Create(Person("DENIED", company, a, position), options: new JsonSerializerOptions())), HttpStatusCode.Forbidden);
        await AssertStatusAsync(read.PutAsJsonAsync($"/api/organization/persons/{person}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(read.PutAsJsonAsync($"/api/organization/persons/{person}/status", new { IsEnabled = false, Reason = "禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(beforeRejected, await HistoryAsync(admin, person));
        await UpdateAsync(writer, person, new { DepartmentId = b });
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}/status", new { IsEnabled = false, Reason = "正式停用" }), HttpStatusCode.OK);
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}/status", new { IsEnabled = true, Reason = "正式启用" }), HttpStatusCode.OK);
        var secondRole = await CreateRoleAsync(admin, "duplicate-source", ["Organization.Person.Update"]);
        await SetRolesAsync(admin, maintainer.Id, ["matrix-m", "duplicate-source"]);
        await SetPermissionsAsync(admin, maintainer.RoleId, [.. Views, "Organization.Person.Create"]);
        await UpdateAsync(writer, person, new { ENAME = "重复来源仍生效" });
        await SetPermissionsAsync(admin, secondRole, []);
        var current = await writer.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.Equal(maintainer.Id, current.GetProperty("id").GetString());
        Assert.DoesNotContain(current.GetProperty("permissions").EnumerateArray(), p => p.GetString() == "Organization.Person.Update");
        var before = await admin.GetStringAsync($"/api/organization/persons/{person}");
        var history = await HistoryAsync(admin, person);
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}", new { ENAME = "撤权后禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}/status", new { IsEnabled = false, Reason = "撤权后禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(before, await admin.GetStringAsync($"/api/organization/persons/{person}"));
        Assert.Equal(history, await HistoryAsync(admin, person));
        await AssertStatusAsync(writer.GetAsync($"/api/organization/persons/{person}"), HttpStatusCode.OK);
        await CreateAsync(writer, "/api/organization/persons", Person("STILL-CREATE", company, a, position));
        // 同会话重新授予立即可用；配置未变而角色停用后不再进入有效并集。
        await SetPermissionsAsync(admin, secondRole, ["Organization.Person.Update"]);
        await UpdateAsync(writer, person, new { ENAME = "重新授予" });
        await AssertStatusAsync(admin.PutAsJsonAsync($"/api/authorization/roles/{secondRole}", new { Name = "停用来源", IsEnabled = false }), HttpStatusCode.OK);
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}", new { ENAME = "停用后禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(admin.PutAsJsonAsync($"/api/authorization/roles/{secondRole}", new { Name = "恢复来源", IsEnabled = true }), HttpStatusCode.OK);
        await fixture.RemoveActivePermissionAsync("Organization.Person.Update");
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}", new { ENAME = "历史目录禁止" }), HttpStatusCode.Forbidden);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "已通过真实判权的在途写允许完成，之后的同Cookie写拒绝")]
    public async Task AuthorizedInFlightRequestMayCommitAfterRevocation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var company = Id(await CreateAsync(admin, "/api/organization/companies", new { ECODE = "INFLIGHT", ENAME = "公司", SortOrder = "0100" }));
        var actor = await CreateActorAsync(fixture, admin, "inflight", Maintenance);
        using var writer = actor.Client;
        var department = Id(await CreateAsync(admin, "/api/organization/departments", new { ECODE = "D", ENAME = "部门", CompanyId = company, SortOrder = "0100" }));
        var person = Id(await CreateAsync(writer, "/api/organization/persons", new { PersonNo = "FLIGHT", ENAME = "起点", CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) => {
            if (change.EventType == "person.updated") { reached.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        var inflight = UpdateAsync(writer, person, new { ENAME = "已判权在途" });
        try {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await SetPermissionsAsync(admin, actor.RoleId, [.. Views, "Organization.Person.Create"]);
        } finally { release.TrySetResult(); }
        await inflight;
        fixture.Fault.AfterRegister = null;
        var history = await HistoryAsync(admin, person);
        await AssertStatusAsync(writer.PutAsJsonAsync($"/api/organization/persons/{person}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(history, await HistoryAsync(admin, person));
        var detail = await writer.GetFromJsonAsync<JsonElement>($"/api/organization/persons/{person}");
        Assert.Equal("已判权在途", detail.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    private static object Person(string number, string company, string department, string position) => new { PersonNo = number, ENAME = number, CompanyId = company, DepartmentId = department, PositionId = position, EmploymentStatus = "Active", IsEnabled = true };
    internal static async Task<(string Id, string RoleId, HttpClient Client)> CreateActorAsync(BusinessAuditHostFixture fixture, HttpClient admin, string code, string[] permissions)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        using var accountResponse = await admin.PostAsJsonAsync("/api/identity/accounts", new { ECODE = code, ENAME = code });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = account.GetProperty("account").GetProperty("eid").GetString()!;
        var role = await CreateRoleAsync(admin, code, permissions);
        await SetRolesAsync(admin, id, [code]);
        var client = await fixture.LoginAsync(code, account.GetProperty("temporaryCredential").GetString()!);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        return (id, role, client);
    }
    private static async Task<string> CreateRoleAsync(HttpClient admin, string code, string[] permissions)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        using var response = await admin.PostAsJsonAsync("/api/authorization/roles", new { Code = code, Name = code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await SetPermissionsAsync(admin, id, permissions);
        return id;
    }
    private static async Task SetRolesAsync(HttpClient admin, string userId, string[] roles)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        await AssertStatusAsync(admin.PutAsJsonAsync($"/api/authorization/users/{userId}/roles", new { RoleCodes = roles }), HttpStatusCode.NoContent);
    }
    private static async Task SetPermissionsAsync(HttpClient admin, string roleId, string[] permissions)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        await AssertStatusAsync(admin.PutAsJsonAsync($"/api/authorization/roles/{roleId}/permissions", new { PermissionCodes = permissions }), HttpStatusCode.NoContent);
    }
    private static async Task<string> HistoryAsync(HttpClient admin, string person) => await admin.GetStringAsync($"/api/audit/business-changes?TargetId={person}");
    private static async Task AssertStatusAsync(Task<HttpResponseMessage> pending, HttpStatusCode expected)
    {
        using var response = await pending;
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
