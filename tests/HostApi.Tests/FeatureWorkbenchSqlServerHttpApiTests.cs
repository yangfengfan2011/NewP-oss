using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureWorkbenchSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实普通主体功能授权与同会话在线撤权")]
    public async Task OrdinaryActor_ViewSaveAndRevocationUseCurrentGrantsAndExactBinding()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var positions = current.GetProperty("featurePositions").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var read = positions.Single(p => !p.EndsWith("/action:save", StringComparison.Ordinal));
        var save = positions.Single(p => p.EndsWith("/action:save", StringComparison.Ordinal));
        var role = await CreateAsync(admin, "/api/authorization/roles", new { Code = "FeatureReader", Name = "普通授权维护" });
        var roleId = role.GetProperty("id").GetString()!;
        var target = await CreateAsync(admin, "/api/authorization/roles", new { Code = "GlobalTarget", Name = "其他角色" });
        var targetId = target.GetProperty("id").GetString()!;
        var account = await CreateAsync(admin, "/api/identity/accounts", new { ECODE = "feature-reader", ENAME = "普通授权者" });
        var actor = account.GetProperty("account").GetProperty("eid").GetString();
        SetPosition(admin, save);
        using (var grant = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        using (var assign = await admin.PutAsJsonAsync($"/api/authorization/users/{actor}/roles", new { RoleCodes = new[] { "FeatureReader" } })) Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        using var ordinary = await fixture.LoginAsync("feature-reader", account.GetProperty("temporaryCredential").GetString()!);
        SetPosition(ordinary, read);
        fixture.Fault.FeatureCommands.Clear();
        using (var list = await ordinary.GetAsync("/api/authorization/workbench/roles?pageIndex=1&pageSize=20")) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(fixture.Fault.FeatureCommands.Where(command => command.Contains("EXISTS", StringComparison.Ordinal) && command.Contains("RoleFunctionGrant", StringComparison.Ordinal)));
        SetPosition(ordinary, save);
        using (var denied = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var grant = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = new[] { read, save } })) Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        SetPosition(ordinary, read);
        using (var wrong = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        SetPosition(ordinary, save);
        using (var allowed = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        using (var revoke = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = Array.Empty<string>() })) Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        SetPosition(ordinary, read);
        using (var revoked = await ordinary.GetAsync("/api/authorization/workbench/catalog")) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    // 准备经正式HTTP，响应失败直接保留最小Problem Details。
    private static async Task<JsonElement> CreateAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    // 请求仅声明当前入口，服务器仍必须与执行端绑定及在线关系同时核对。
    private static void SetPosition(HttpClient client, string position)
    {
        client.DefaultRequestHeaders.Remove("X-NewP-Feature");
        client.DefaultRequestHeaders.Add("X-NewP-Feature", position);
    }
}
