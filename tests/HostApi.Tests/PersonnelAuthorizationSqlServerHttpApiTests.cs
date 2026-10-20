using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.BusinessAuditSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class PersonnelAuthorizationSqlServerHttpApiTests
{
    private static FeaturePoint[] Views(FeatureHttpTestRequests http) => [.. new[] { "structure", "companies", "departments", "positions", "persons" }.Select(tab => http.Organization(tab)), http.Organization("persons", "export", "list")];
    private static FeaturePoint[] Maintenance(FeatureHttpTestRequests http) => [.. Views(http), http.Organization("persons", "create", "list.pageActions"), http.Organization("persons", "edit", "list.rowActions"), http.Organization("persons", "enable", "list.rowActions"), http.Organization("persons", "disable", "list.rowActions")];

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实三账号完整矩阵及原Cookie撤销全部权限来源")]
    public async Task RealSessions_EnforceMatrixAndRevokeAllUpdateSourcesWithoutRelogin()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var company = Id(await http.CreateButtonAsync(admin, http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies", new { ECODE = "MATRIX", ENAME = "公司", SortOrder = "0100" }));
        var a = Id(await http.CreateButtonAsync(admin, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "A", ENAME = "部门A", CompanyId = company, SortOrder = "0100" }));
        var b = Id(await http.CreateButtonAsync(admin, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "B", ENAME = "部门B", CompanyId = company, SortOrder = "0200" }));
        var position = Id(await http.CreateButtonAsync(admin, http.Organization("positions", "create", "list.pageActions"), "/api/organization/positions", new { ECODE = "P", ENAME = "岗位", SortOrder = "0100" }));
        var maintainer = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture, admin, "matrix-m", "matrix-m", "matrix-m", "matrix-m", Maintenance(http));
        var reader = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture, admin, "matrix-r", "matrix-r", "matrix-r", "matrix-r", Views(http));
        using var writer = maintainer.Client;
        using var read = reader.Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        var edit = http.Organization("persons", "edit", "list.rowActions");
        var disable = http.Organization("persons", "disable", "list.rowActions");
        var person = Id(await new FeatureHttpTestRequests(fixture).CreateButtonAsync(writer, create, "/api/organization/persons", Person("MATRIX-1", company, a, position)));
        foreach (var client in new[] { writer, read })
        {
            foreach (var (route, id) in new[] { ("companies", company), ("departments", a), ("positions", position), ("persons", person) })
            {
                await AssertStatusAsync(SendAsync(client, http.Organization(route), HttpMethod.Get, $"/api/organization/{route}"), HttpStatusCode.OK);
                await AssertStatusAsync(SendAsync(client, http.Organization(route), HttpMethod.Get, $"/api/organization/{route}/{id}"), HttpStatusCode.OK);
                var export = route == "persons" ? http.Organization(route, "export", "list") : http.Organization(route);
                await AssertStatusAsync(SendAsync(client, export, HttpMethod.Post, $"/api/organization/{route}/export", new { mode = "current", columns = new[] { new { field = "ENAME", title = "名称" } } }), route == "persons" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            }
            foreach (var route in new[] { "/api/trees/organization.structure/roots?scope=global", $"/api/trees/organization.structure/children?scope=global&nodeKey=department:{a}", $"/api/trees/organization.structure/path?scope=global&nodeKey=person:{person}", "/api/trees/organization.structure/search?scope=global&keyword=MATRIX&page=1&pageSize=20" })
                await AssertStatusAsync(SendAsync(client, http.Organization("structure"), HttpMethod.Get, route), HttpStatusCode.OK);
            foreach (var route in new[] { "/api/organization/person-lookups/companies?page=1&pageSize=1", $"/api/organization/person-lookups/departments?companyId={company}&page=1&pageSize=1", "/api/organization/positions/options" })
                await AssertStatusAsync(SendAsync(client, create, HttpMethod.Get, route), client == writer ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            foreach (var route in new[] { "/api/organization/department-lookups/companies", $"/api/organization/department-lookups/parents?companyId={company}" })
                await AssertStatusAsync(SendAsync(client, http.Organization("departments"), HttpMethod.Get, route), HttpStatusCode.OK);
            await AssertStatusAsync(client.GetAsync("/api/authorization/permissions"), HttpStatusCode.NotFound);
            foreach (var route in new[] { "/api/identity/accounts", "/api/authorization/roles", "/api/audit/business-changes" })
                await AssertStatusAsync(client.GetAsync(route), HttpStatusCode.Forbidden);
            await AssertStatusAsync(http.SendButtonAsync(client, http.Organization("persons", "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/persons/{person}"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(http.SendButtonAsync(client, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put, $"/api/organization/persons/{person}/user-binding", new { UserId = reader.AccountId, Reason = "不应绑定" }), HttpStatusCode.Forbidden);
            foreach (var (route, id) in new[] { ("companies", company), ("departments", a), ("positions", position) })
            {
                await AssertStatusAsync(http.SendButtonAsync(client, http.Organization(route, "create", "list.pageActions"), HttpMethod.Post, $"/api/organization/{route}", new { ECODE = "DENIED", ENAME = "禁止" }), HttpStatusCode.Forbidden);
                await AssertStatusAsync(http.SendButtonAsync(client, http.Organization(route, "edit", "list.rowActions"), HttpMethod.Put, $"/api/organization/{route}/{id}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
                await AssertStatusAsync(http.SendButtonAsync(client, http.Organization(route, "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/{route}/{id}"), HttpStatusCode.Forbidden);
            }
            await AssertStatusAsync(http.SendButtonAsync(client, http.Identity("create"), HttpMethod.Post, "/api/identity/accounts", new { ECODE = "denied", ENAME = "禁止" }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(http.SendButtonAsync(client, http.Workbench("authorization.workbench.save"), HttpMethod.Put, $"/api/authorization/workbench/roles/{maintainer.RoleId}", new { positions = Array.Empty<string>() }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(http.SendButtonAsync(client, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{maintainer.AccountId}/roles", new { RoleCodes = Array.Empty<string>() }), HttpStatusCode.Forbidden);
        }
        var beforeRejected = await HistoryAsync(http, admin, person);
        await AssertStatusAsync(SendAsync(read, create, HttpMethod.Post, "/api/organization/persons", Person("DENIED", company, a, position)), HttpStatusCode.Forbidden);
        await AssertStatusAsync(SendAsync(read, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(read, disable, HttpMethod.Put, $"/api/organization/persons/{person}/status/disable", new { isEnabled = false, reason = "禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(beforeRejected, await HistoryAsync(http, admin, person));
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { DepartmentId = b }), HttpStatusCode.OK);
        await AssertStatusAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(writer, disable, HttpMethod.Put, $"/api/organization/persons/{person}/status/disable", new { isEnabled = false, reason = "正式停用" }), HttpStatusCode.OK);
        await AssertStatusAsync(http.SendButtonAsync(writer, http.Organization("persons", "enable", "list.rowActions"), HttpMethod.Put, $"/api/organization/persons/{person}/status/enable", new { isEnabled = true, reason = "正式启用" }), HttpStatusCode.OK);
        var secondRole = await CreateRoleAsync(http, admin, "duplicate-source", [edit]);
        await SetRolesAsync(http, admin, maintainer.AccountId, ["matrix-m", "duplicate-source"]);
        await http.GrantAsync(admin, maintainer.RoleId, [.. Views(http), create]);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "重复来源仍生效" }), HttpStatusCode.OK);
        await http.GrantAsync(admin, secondRole, []);
        var current = await writer.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.Equal(maintainer.AccountId, current.GetProperty("id").GetString());
        Assert.DoesNotContain(current.GetProperty("featurePositions").EnumerateArray(), point => point.GetString() == edit.Position);
        var before = await ReadTextAsync(admin, http.Organization("persons"), $"/api/organization/persons/{person}");
        var history = await HistoryAsync(http, admin, person);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "撤权后禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(new FeatureHttpTestRequests(fixture).SendButtonAsync(writer, disable, HttpMethod.Put, $"/api/organization/persons/{person}/status/disable", new { isEnabled = false, reason = "撤权后禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(before, await ReadTextAsync(admin, http.Organization("persons"), $"/api/organization/persons/{person}"));
        Assert.Equal(history, await HistoryAsync(http, admin, person));
        await AssertStatusAsync(SendAsync(writer, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{person}"), HttpStatusCode.OK);
        await new FeatureHttpTestRequests(fixture).CreateButtonAsync(writer, create, "/api/organization/persons", Person("STILL-CREATE", company, a, position));
        await http.GrantAsync(admin, secondRole, edit);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "重新授予" }), HttpStatusCode.OK);
        await AssertStatusAsync(http.SendButtonAsync(admin, http.Role("edit"), HttpMethod.Put, $"/api/authorization/roles/{secondRole}", new { Name = "停用来源", IsEnabled = false }), HttpStatusCode.OK);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "停用后禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(http.SendButtonAsync(admin, http.Role("edit"), HttpMethod.Put, $"/api/authorization/roles/{secondRole}", new { Name = "恢复来源", IsEnabled = true }), HttpStatusCode.OK);
        // 正式删除永久撤销普通分配；同名重新引入只能由管理员重新授予。
        await fixture.RemoveActiveFeatureAsync(edit.Position);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "删除后禁止" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(SendAsync(writer, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{person}"), HttpStatusCode.OK);
        await fixture.RestoreFeatureCatalogAsync();
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "重加不恢复" }), HttpStatusCode.Forbidden);
        await http.GrantAsync(admin, secondRole, edit);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "明确重新授予" }), HttpStatusCode.OK);
        await fixture.RepeatFeatureSynchronizationAsync();
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "已通过真实判权的在途写允许完成，之后的同Cookie写拒绝")]
    public async Task AuthorizedInFlightRequestMayCommitAfterRevocation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Organization("persons", "create", "list.pageActions");
        var edit = http.Organization("persons", "edit", "list.rowActions");
        var company = Id(await http.CreateButtonAsync(admin, http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies", new { ECODE = "INFLIGHT", ENAME = "公司", SortOrder = "0100" }));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture, admin, "inflight", "inflight", "inflight", "inflight", Maintenance(http));
        using var writer = actor.Client;
        var department = Id(await http.CreateButtonAsync(admin, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "D", ENAME = "部门", CompanyId = company, SortOrder = "0100" }));
        var person = Id(await new FeatureHttpTestRequests(fixture).CreateButtonAsync(writer, create, "/api/organization/persons", new { PersonNo = "FLIGHT", ENAME = "起点", CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) => { if (change.EventType == "person.updated") { reached.TrySetResult(); await release.Task.WaitAsync(token); } };
        var inflight = AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "已判权在途" }), HttpStatusCode.OK);
        try { await reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); await http.GrantAsync(admin, actor.RoleId, [.. Views(http), create]); }
        finally { release.TrySetResult(); }
        await inflight;
        fixture.Fault.AfterRegister = null;
        var history = await HistoryAsync(http, admin, person);
        await AssertStatusAsync(SendAsync(writer, edit, HttpMethod.Put, $"/api/organization/persons/{person}", new { ENAME = "禁止" }), HttpStatusCode.Forbidden);
        Assert.Equal(history, await HistoryAsync(http, admin, person));
        var detail = await ReadAsync(writer, http.Organization("persons"), $"/api/organization/persons/{person}");
        Assert.Equal("已判权在途", detail.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    private static object Person(string number, string company, string department, string position) => new { PersonNo = number, ENAME = number, CompanyId = company, DepartmentId = department, PositionId = position, EmploymentStatus = "Active", IsEnabled = true };
    private static async Task<string> CreateRoleAsync(FeatureHttpTestRequests http, HttpClient admin, string code, FeaturePoint[] features)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        using var response = await http.SendButtonAsync(admin, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = code, Name = code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await http.GrantAsync(admin, id, features);
        return id;
    }
    private static async Task SetRolesAsync(FeatureHttpTestRequests http, HttpClient admin, string userId, string[] roles)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        await AssertStatusAsync(http.SendButtonAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{userId}/roles", new { RoleCodes = roles }), HttpStatusCode.NoContent);
    }
    private static async Task<string> HistoryAsync(FeatureHttpTestRequests http, HttpClient admin, string person) => await FeatureHttpTestRequests.ReadTextAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={person}");
    private static async Task AssertStatusAsync(Task<HttpResponseMessage> pending, HttpStatusCode expected)
    {
        using var response = await pending;
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
