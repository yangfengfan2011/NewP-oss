using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationAccountAliasSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "成功绑定SQL识别的账号别名后仍能通过正式受限登录")]
    public async Task Binding_WithAccountEidCaseAlias_PreservesCanonicalAdmission()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "ACCOUNT-ALIAS", ENAME = "账号别名公司", SortOrder = "01" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "ACCOUNT-ALIAS", ENAME = "账号别名部门", CompanyId = company, SortOrder = "01" }));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "ACCOUNT-ALIAS", ENAME = "账号别名人员", CompanyId = company, DepartmentId = department, EmploymentStatus = "Active", IsEnabled = true }));
        var created = await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "account-alias", ENAME = "账号别名主体" });
        var account = created.GetProperty("account");
        var canonical = account.GetProperty("eid").GetString()!;
        var alias = canonical.ToLowerInvariant();
        Assert.NotEqual(canonical, alias);
        output.WriteLine($"personCanonical={person}; accountCanonical={canonical}; accountAlias={alias}");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT t.name, c.name, c.collation_name FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id WHERE (t.name=N'IdentityAccount' AND c.name=N'EID') OR (t.name=N'Person' AND c.name=N'AccountEID') ORDER BY t.name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) output.WriteLine($"actualCollation {reader.GetString(0)}.{reader.GetString(1)}={reader.GetString(2)}");
        }
        var directory = scope.ServiceProvider.GetRequiredService<IIdentityAccountDirectory>();
        Assert.Equal(canonical, (await directory.FindAsync(alias))!.AccountEid);
        using var bound = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = alias, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "仅账号标识使用SQL可识别别名" });
        output.WriteLine($"bindingStatus={(int)bound.StatusCode}; bindingBody={await bound.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
        Assert.Equal(canonical, (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountEid").GetString());
        var stored = await db.Set<Dictionary<string, object?>>("Person").AsNoTracking().SingleAsync();
        output.WriteLine($"storedPerson.AccountEID={stored["AccountEID"]}; identityCanonical={(await directory.FindAsync(alias))!.AccountEid}; identityBound={(await directory.FindAsync(canonical))!.IsPersonnelBound}");
        var view = await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync();
        var current = view.FindByAccount(canonical);
        output.WriteLine($"findByAccountCanonicalFound={current is not null}; findByAccountAliasFound={view.FindByAccount(alias) is not null}");

        var boundRevision = (await directory.FindAsync(canonical))!.Revision;
        foreach (var sameTarget in new[] { alias, canonical })
        {
            using var repeated = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
                $"/api/organization/persons/{person}/user-binding", new { accountEid = sameTarget, expectedRevision = 0, reason = "不覆盖首次原因" });
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(canonical, repeatedBody.GetProperty("accountEid").GetString());
            Assert.Equal("仅账号标识使用SQL可识别别名", repeatedBody.GetProperty("reason").GetString());
            Assert.Equal(boundRevision, (await directory.FindAsync(canonical))!.Revision);
        }
        var otherAccount = (await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "other-target", ENAME = "不同目标" })).GetProperty("account");
        using (var conflict = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = otherAccount.GetProperty("eid").GetString(), expectedRevision = otherAccount.GetProperty("revision").GetInt64(), reason = "不得改绑" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(PersonUserBindingFailureCodes.PersonAlreadyBound, (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using var client = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = "account-alias", Password = created.GetProperty("temporaryCredential").GetString() });
        output.WriteLine($"loginStatus={(int)login.StatusCode}; loginBody={await login.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.NotNull(current);
        Assert.Equal(canonical, stored["AccountEID"]);
        using var authenticated = await fixture.LoginAsync("account-alias", created.GetProperty("temporaryCredential").GetString()!);
        using var currentUser = await authenticated.GetAsync("/api/auth/current-user");
        Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);
    }
}
