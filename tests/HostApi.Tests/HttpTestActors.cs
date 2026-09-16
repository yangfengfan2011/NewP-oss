using System.Net;
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
            new { Code = roleCode, Name = roleName });
        var account = await FeatureHttpTestRequests.CreateAsync(
            administrator,
            http.Identity("create"),
            "/api/identity/accounts",
            new { ECODE = accountCode, ENAME = accountName });
        var accountId = account.GetProperty("account").GetProperty("eid").GetString()!;
        using var assignment = await FeatureHttpTestRequests.SendAsync(
            administrator,
            http.Role("userRoles"),
            HttpMethod.Put,
            $"/api/authorization/users/{accountId}/roles",
            new { RoleCodes = new[] { roleCode } });
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
}
