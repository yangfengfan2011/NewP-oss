using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "目标完整链拦截新增调入换父，停用范围可迁出且岗位停用不阻断")]
    public async Task TargetChain_RejectsInvalidTargetsAndAllowsMovingOutOfDisabledScope()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        var child = await CreateDepartmentAsync(admin, http, prepared.Company, "CHILD", prepared.Department);
        var leaf = await CreateDepartmentAsync(admin, http, prepared.Company, "LEAF", child);
        var target = await CreateDepartmentAsync(admin, http, prepared.Company, "TARGET");
        var actor = await PrepareAccountAsync(admin, http, prepared.Company, leaf, "moving");
        using var old = await fixture.LoginAsync("moving", actor.Password);
        foreach (var validParent in new[] { target, prepared.Department })
        {
            using var changed = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/departments/{child}", new { ParentDepartmentId = validParent });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await old.GetAsync("/api/auth/current-user")).StatusCode);
            Assert.Equal(new[] { child, validParent }, (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync())
                .FindByAccount(actor.Account)!.Ancestors.Select(row => (string)row["EID"]!));
        }
        await SetOrganizationStateAsync(admin, http, "departments", prepared.Department, false);

        using (var create = await SendAsync(admin, http.Organization("persons", "create", "list.pageActions"), HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "BLOCKED", ENAME = "不应调入", CompanyId = prepared.Company, DepartmentId = leaf, EmploymentStatus = "Active", IsEnabled = true }))
            await AssertTargetProblemAsync(create, "Organization.Person.DepartmentChainDisabled", "DepartmentId");
        using (var move = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{prepared.Person}", new { DepartmentId = leaf }))
            await AssertTargetProblemAsync(move, "Organization.Person.DepartmentChainDisabled", "DepartmentId");
        using (var reparent = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{target}", new { ParentDepartmentId = leaf }))
            await AssertTargetProblemAsync(reparent, "Organization.Department.ParentChainDisabled", "ParentDepartmentId");

        // 标准Drawer可能回传未改变的父级；普通维护和停复均不能因此变成调入校验。
        using (var unchanged = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{child}", new { ENAME = "停用范围内维护", ParentDepartmentId = prepared.Department }))
            Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        await UpdateAsync(admin, http, actor.Person, new { ENAME = "保持原归属" });
        using (var moveOut = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{child}", new { ParentDepartmentId = target }))
            Assert.True(moveOut.IsSuccessStatusCode, await moveOut.Content.ReadAsStringAsync());
        var view = await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync();
        Assert.Equal(new[] { child, target }, view.FindByAccount(actor.Account)!.Ancestors.Select(row => (string)row["EID"]!));
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/auth/current-user")).StatusCode);
        using var fresh = fixture.CreateClient();
        await LoginAccountAsync(fresh, "moving");
        await UpdateAsync(admin, http, prepared.Person, new { DepartmentId = target });

        var position = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("positions", "create", "list.pageActions"),
            "/api/organization/positions", new { ENAME = "独立岗位" }));
        await UpdateAsync(admin, http, actor.Person, new { PositionId = position });
        await SetOrganizationStateAsync(admin, http, "positions", position, false);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/current-user")).StatusCode);

        var otherCompany = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "OTHER", ENAME = "另一公司", SortOrder = "02" }));
        var otherDepartment = await CreateDepartmentAsync(admin, http, otherCompany, "OTHER");
        await SetOrganizationStateAsync(admin, http, "companies", prepared.Company, false);
        using (var rootMove = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{child}", new { ParentDepartmentId = (string?)null }))
            await AssertTargetProblemAsync(rootMove, "Organization.Department.CompanyDisabled", "CompanyId", HttpStatusCode.Conflict);
        await UpdateAsync(admin, http, actor.Person, new { CompanyId = otherCompany, DepartmentId = otherDepartment });
        Assert.Equal(otherCompany, (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.Account)!.Company!["EID"]);
        using (var crossCompany = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{child}", new { CompanyId = otherCompany }))
            Assert.Equal(HttpStatusCode.BadRequest, crossCompany.StatusCode);
        using (var delete = await SendAsync(admin, http.Organization("companies", "delete", "list.rowActions"), HttpMethod.Delete,
            $"/api/organization/companies/{prepared.Company}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
            Assert.Equal("Organization.Company.DeleteBlockedByReference", (await delete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "上级多账号撤销和组织写入任一失败共同回滚")]
    public async Task AncestorInterruption_WhenRevocationOrOrganizationCommitFails_RollsBackEveryAccount()
    {
        var control = new Control();
        var writeFault = new OrganizationUpdateFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            ConfigureControl(services, control);
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(writeFault));
        });
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        var child = await CreateDepartmentAsync(admin, http, prepared.Company, "CHILD", prepared.Department);
        var actor = await PrepareAccountAsync(admin, http, prepared.Company, child, "rollback-child");
        using var rootClient = await fixture.LoginAsync("admitted", prepared.Password);
        using var childClient = await fixture.LoginAsync("rollback-child", actor.Password);
        using var rootSecond = fixture.CreateClient();
        using var childSecond = fixture.CreateClient();
        await LoginAccountAsync(rootSecond, "admitted");
        await LoginAccountAsync(childSecond, "rollback-child");
        foreach (var resource in new[] { "companies", "departments" })
        foreach (var stage in new[] { "revoke", "commit", "write" })
        {
            control.LastRevocationCount = 0;
            control.FailAfterRevocation = stage == "revoke";
            fixture.Fault.Mode = stage == "commit" ? "commit" : "";
            writeFault.Enabled = stage == "write";
            writeFault.Hits = 0;
            var id = resource == "companies" ? prepared.Company : prepared.Department;
            using var failed = await SendAsync(admin, http.Organization(resource, "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/{resource}/{id}", new { IsEnabled = false });
            Assert.True(failed.StatusCode == HttpStatusCode.InternalServerError, $"{resource}/{stage}: {failed.StatusCode}; hits={writeFault.Hits}");
            control.FailAfterRevocation = false;
            fixture.Fault.Mode = "";
            writeFault.Enabled = false;
            Assert.Equal(stage == "write" ? 1 : 0, writeFault.Hits);
            Assert.Equal(stage == "write" ? 0 : 4, control.LastRevocationCount);
            foreach (var client in new[] { rootClient, rootSecond, childClient, childSecond })
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/current-user")).StatusCode);
            var view = await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync();
            Assert.Equal(true, view.Find(OrganizationRecordKind.Company, prepared.Company)!["IsEnabled"]);
            Assert.Equal(true, view.Find(OrganizationRecordKind.Department, prepared.Department)!["IsEnabled"]);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var model = resource == "companies" ? "Company" : "Department";
            Assert.True(await db.Set<Dictionary<string, object?>>("Organization/" + model).AsNoTracking()
                .Where(row => EF.Property<string>(row, "EID") == id).Select(row => EF.Property<bool>(row, "IsEnabled")).SingleAsync());
            var ids = new[] { prepared.Account, actor.Account };
            Assert.Equal(4, await db.Set<Dictionary<string, object?>>("Authentication/AuthenticationSession")
                .CountAsync(row => ids.Contains(EF.Property<string>(row, "AccountEID")) && EF.Property<DateTime?>(row, "RevokedAt") == null));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "上级子树停用与登录首次改密轮换的双向确定交错")]
    public async Task AncestorInterruption_OrdersAllSessionCreationEntrypoints()
    {
        foreach (var operation in new[] { "login", "first-password", "rotation" })
        foreach (var creationFirst in new[] { true, false })
            await AssertCreationOrderAsync(operation, creationFirst, ancestor: true);
    }

    private static async Task AssertTargetProblemAsync(HttpResponseMessage response, string code, string field, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.GetProperty("statusCode").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("reasonKey").GetString()));
        var fieldError = Assert.Single(problem.GetProperty("fieldErrors").EnumerateArray());
        Assert.Equal(field, fieldError.GetProperty("field").GetString());
        Assert.Equal(code, fieldError.GetProperty("code").GetString());
    }

    private sealed class OrganizationUpdateFault : DbCommandInterceptor
    {
        public bool Enabled;
        public int Hits;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && (command.CommandText.Contains("UPDATE", StringComparison.Ordinal) && (command.CommandText.Contains("[Company]", StringComparison.Ordinal)
                || command.CommandText.Contains("[Department]", StringComparison.Ordinal))))
            {
                Hits++;
                throw new InvalidOperationException("injected organization update failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "公司和上级部门停复覆盖多层多账号，旧会话永久失效且下级状态独立")]
    public async Task AncestorDisableRestore_RevokesEveryDescendantWithoutRequestsAndPreservesIndependentStates()
    {
        foreach (var resource in new[] { "companies", "departments" })
        {
            await using var fixture = await BusinessAuditHostFixture.CreateAsync();
            using var admin = await fixture.LoginAdministratorAsync();
            var http = new FeatureHttpTestRequests(fixture);
            var prepared = await PrepareAsync(admin, http);
            var child = await CreateDepartmentAsync(admin, http, prepared.Company, "CHILD", prepared.Department);
            var leaf = await CreateDepartmentAsync(admin, http, prepared.Company, "LEAF", child);
            var disabled = await CreateDepartmentAsync(admin, http, prepared.Company, "DISABLED", prepared.Department);
            var childActor = await PrepareAccountAsync(admin, http, prepared.Company, child, "child");
            var leafActor = await PrepareAccountAsync(admin, http, prepared.Company, leaf, "leaf");
            var outsideCompany = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
                "/api/organization/companies", new { ECODE = "OUTSIDE", ENAME = "范围外公司", SortOrder = "02" }));
            var outsideDepartment = await CreateDepartmentAsync(admin, http, outsideCompany, "OUTSIDE");
            var outsideActor = await PrepareAccountAsync(admin, http, outsideCompany, outsideDepartment, "outside");
            using var outside = await fixture.LoginAsync("outside", outsideActor.Password);
            using var rootClient = await fixture.LoginAsync("admitted", prepared.Password);
            using var childClient = await fixture.LoginAsync("child", childActor.Password);
            using var leafClient = await fixture.LoginAsync("leaf", leafActor.Password);
            using var rootSecond = fixture.CreateClient();
            using var childSecond = fixture.CreateClient();
            using var leafSecond = fixture.CreateClient();
            await LoginAccountAsync(rootSecond, "admitted");
            await LoginAccountAsync(childSecond, "child");
            await LoginAccountAsync(leafSecond, "leaf");
            await SetOrganizationStateAsync(admin, http, "departments", disabled, false);
            var target = resource == "companies" ? prepared.Company : prepared.Department;
            await SetOrganizationStateAsync(admin, http, resource, target, false);
            // 一个会话立即检查，其余五个在停用期间完全不发请求。
            Assert.Equal(HttpStatusCode.Unauthorized, (await rootClient.GetAsync("/api/auth/current-user")).StatusCode);
            await SetOrganizationStateAsync(admin, http, resource, target, true);
            foreach (var client in new[] { rootSecond, childClient, childSecond, leafClient, leafSecond })
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/current-user")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await outside.GetAsync("/api/auth/current-user")).StatusCode);
            var view = await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync();
            Assert.Equal(false, view.Find(OrganizationRecordKind.Department, disabled)!["IsEnabled"]);
            foreach (var id in new[] { child, leaf }) Assert.Equal(true, view.Find(OrganizationRecordKind.Department, id)!["IsEnabled"]);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var account in new[] { prepared.Account, childActor.Account, leafActor.Account })
            {
                Assert.Equal(true, view.FindByAccount(account)!.Person["IsEnabled"]);
                Assert.Equal(IdentityAccountStatus.Enabled, (await scope.ServiceProvider.GetRequiredService<IIdentityAccountDirectory>().FindAsync(account))!.Status);
                var sessions = await db.Set<Dictionary<string, object?>>("Authentication/AuthenticationSession").AsNoTracking()
                    .Where(row => EF.Property<string>(row, "AccountEID") == account).ToListAsync();
                Assert.Equal(3, sessions.Count);
                Assert.All(sessions, row => Assert.NotNull(row["RevokedAt"]));
            }
            using var fresh = fixture.CreateClient();
            await LoginAccountAsync(fresh, "leaf");
        }
    }

    private static async Task<string> CreateDepartmentAsync(HttpClient admin, FeatureHttpTestRequests http, string company, string code, string? parent = null) =>
        BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = code, ENAME = code, CompanyId = company, ParentDepartmentId = parent, SortOrder = "01" }));

    private static async Task<(string Person, string Account, string Password)> PrepareAccountAsync(
        HttpClient admin, FeatureHttpTestRequests http, string company, string department, string code)
    {
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = code, ENAME = code, CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var created = await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = code, ENAME = code });
        var account = created.GetProperty("account");
        using var bound = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = account.GetProperty("eid").GetString(), expectedRevision = account.GetProperty("revision").GetInt64(), reason = "子树准入准备" });
        Assert.True(bound.IsSuccessStatusCode, await bound.Content.ReadAsStringAsync());
        return (person, account.GetProperty("eid").GetString()!, created.GetProperty("temporaryCredential").GetString()!);
    }

    private static async Task SetOrganizationStateAsync(HttpClient admin, FeatureHttpTestRequests http, string resource, string id, bool enabled)
    {
        using var response = await SendAsync(admin, http.Organization(resource, "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/{resource}/{id}", new { IsEnabled = enabled });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task LoginAccountAsync(HttpClient client, string code)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = "Audit-test-secure-938!" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
