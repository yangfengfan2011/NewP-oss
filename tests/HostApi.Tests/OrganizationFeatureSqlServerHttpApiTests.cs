using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationFeatureSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实 Organization 标准创建位置端到端授权")]
    public async Task PersonCreate_UsesDeclaredPositionAndCannotBorrowReadOrUpdate()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var positions = current.GetProperty("featurePositions").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var workbenchRead = positions.Single(position => position.StartsWith("Authorization/", StringComparison.Ordinal) && !position.EndsWith("/action:save", StringComparison.Ordinal));
        var workbenchSave = positions.Single(position => position.StartsWith("Authorization/", StringComparison.Ordinal) && position.EndsWith("/action:save", StringComparison.Ordinal));
        Position(admin, workbenchRead);
        var catalog = await admin.GetFromJsonAsync<JsonElement>("/api/authorization/workbench/catalog");
        var create = catalog.GetProperty("points").EnumerateArray().Single(point => point.GetProperty("position").GetString()!.EndsWith("/tab:persons/surface:list.pageActions/action:create", StringComparison.Ordinal));
        var createPosition = create.GetProperty("position").GetString()!;
        var dependencies = create.GetProperty("entryDependencies").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var role = await BusinessAuditSqlServerHttpApiTests.CreateAsync(admin, "/api/authorization/roles", new { Code = "PersonCreator", Name = "人员创建者" });
        var account = await BusinessAuditSqlServerHttpApiTests.CreateAsync(admin, "/api/identity/accounts", new { ECODE = "person-creator", ENAME = "人员创建者" });
        Position(admin, workbenchSave);
        using (var grant = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{role.GetProperty("id").GetString()}", new { positions = dependencies.Append(createPosition).ToArray() }))
            Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        using (var membership = await admin.PutAsJsonAsync($"/api/authorization/users/{account.GetProperty("account").GetProperty("eid").GetString()}/roles", new { RoleCodes = new[] { "PersonCreator" } }))
            Assert.Equal(HttpStatusCode.NoContent, membership.StatusCode);
        using var user = await fixture.LoginAsync("person-creator", account.GetProperty("temporaryCredential").GetString()!);
        Position(user, createPosition);
        using var created = await user.PostAsJsonAsync("/api/organization/persons", new { PersonNo = "FEATURE-001", ENAME = "位置创建", EmploymentStatus = "FullTime", IsEnabled = true }, new JsonSerializerOptions());
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Position(user, dependencies[^1]);
        using var wrong = await user.PostAsJsonAsync("/api/organization/persons", new { PersonNo = "FEATURE-002", ENAME = "不得创建", EmploymentStatus = "FullTime", IsEnabled = true }, new JsonSerializerOptions());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        user.DefaultRequestHeaders.Remove("X-NewP-Feature");
        using var bypass = await user.PostAsJsonAsync("/api/organization/persons", new { PersonNo = "FEATURE-003", ENAME = "不得绕行", EmploymentStatus = "FullTime", IsEnabled = true }, new JsonSerializerOptions());
        Assert.Equal(HttpStatusCode.Forbidden, bypass.StatusCode);
    }

    private static void Position(HttpClient client, string position)
    {
        client.DefaultRequestHeaders.Remove("X-NewP-Feature");
        client.DefaultRequestHeaders.Add("X-NewP-Feature", position);
    }
}
