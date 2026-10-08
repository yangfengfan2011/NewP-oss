using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

internal sealed record WorkspaceAcceptanceActor(string AccountId, string ECode, string Password);

/// <summary>通过正式 Identity、Organization、Authorization 和 Workspace API 准备普通账号与公共根。</summary>
internal static class WorkspaceAcceptanceFixture
{
    internal const string CookieName = "ICP.Platform.WorkspaceAcceptance";

    internal static HttpClient CreateClient(int port) => new(new HttpClientHandler { CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}")
    };

    internal static async Task LoginAndChangePasswordAsync(HttpClient client, string ecode, string temporaryPassword,
        string permanentPassword, CancellationToken token)
    {
        var login = await SendAsync(client, HttpMethod.Post, "/api/auth/login",
            new { ecode, password = temporaryPassword }, null, token);
        await SendAsync(client, HttpMethod.Post, "/api/identity/me/password-change",
            new { currentPassword = temporaryPassword, newPassword = permanentPassword, expectedRevision = login.GetProperty("revision").GetInt64() }, null, token);
    }

    internal static Task<JsonElement> LoginAsync(HttpClient client, WorkspaceAcceptanceActor actor, CancellationToken token) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/login", new { ecode = actor.ECode, password = actor.Password }, null, token);

    internal static async Task<WorkspaceAcceptanceActor> PrepareAsync(HttpClient administrator,
        AcceptanceFeatureCatalog features, string runId, Action<string> registerSecret, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        await SendAsync(administrator, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, entry.Position, token);
        var suffix = runId[..8];
        var ecode = $"workspace.{suffix}";
        var account = await SendAsync(administrator, HttpMethod.Post, "/api/identity/accounts",
            new { ECODE = ecode, ENAME = $"工作空间验收账号 {suffix}" },
            features.Action(features.Behavior("Identity", "identity.account.read"), "create", "list.pageActions").Position, token);
        var accountData = account.GetProperty("account");
        var accountId = accountData.GetProperty("eid").GetString()!;
        var temporaryCredential = account.GetProperty("temporaryCredential").GetString()!;
        registerSecret(temporaryCredential);
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        registerSecret(password);

        var company = await SendAsync(administrator, HttpMethod.Post, "/api/organization/companies",
            new { ECODE = $"WS-{suffix}", ENAME = $"工作空间验收公司 {suffix}", SortOrder = "9999" },
            features.Action(features.OrganizationTab("companies"), "create", "list.pageActions").Position, token);
        var companyId = EntityId(company);
        var department = await SendAsync(administrator, HttpMethod.Post, "/api/organization/departments",
            new { ECODE = $"WS-{suffix}", ENAME = $"工作空间验收部门 {suffix}", CompanyId = companyId, SortOrder = "9999" },
            features.Action(features.OrganizationTab("departments"), "create", "list.pageActions").Position, token);
        var person = await SendAsync(administrator, HttpMethod.Post, "/api/organization/persons",
            new { PersonNo = $"WS-{suffix}", ENAME = $"工作空间验收人员 {suffix}", CompanyId = companyId,
                DepartmentId = EntityId(department), EmploymentStatus = "Active", IsEnabled = true },
            features.Action(features.OrganizationTab("persons"), "create", "list.pageActions").Position, token);
        await SendAsync(administrator, HttpMethod.Put, $"/api/organization/persons/{EntityId(person)}/user-binding",
            new { accountEid = accountId, expectedRevision = accountData.GetProperty("revision").GetInt64(), reason = "准备工作空间普通验收主体" },
            features.Action(features.OrganizationTab("persons"), "linkUser", "detail.custom").Position, token);

        var roleCode = $"WorkspaceGate{suffix}";
        var roleEntry = features.Behavior("Authorization", "authorization.role.read");
        var role = await SendAsync(administrator, HttpMethod.Post, "/api/authorization/roles",
            new { Code = roleCode, Name = $"工作空间验收角色 {suffix}" },
            features.Action(roleEntry, "create", "list.pageActions").Position, token);
        var roleId = role.GetProperty("id").GetString()!;
        await SendAsync(administrator, HttpMethod.Put, $"/api/authorization/workbench/roles/{roleId}",
            new { positions = features.GrantSet(entry) }, features.Behavior("Authorization", "authorization.workbench.save").Position, token);
        await SendAsync(administrator, HttpMethod.Put, $"/api/authorization/users/{accountId}/roles",
            new { RoleCodes = new[] { roleCode } }, features.Action(roleEntry, "userRoles", "list.rowActions").Position, token);
        using var ordinary = CreateClient(administrator.BaseAddress!.Port);
        await LoginAndChangePasswordAsync(ordinary, ecode, temporaryCredential, password, token);
        return new(accountId, ecode, password);
    }

    // 正式命令保留空请求体；所有写请求取得同会话防伪令牌，Secure Cookie 仅在 loopback client 显式回放。
    internal static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body, string? featurePosition, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        if (featurePosition is not null) request.Headers.Add("X-NewP-Feature", featurePosition);
        if (method != HttpMethod.Get)
        {
            using var antiforgeryResponse = await client.GetAsync("/api/auth/antiforgery-token", token);
            var antiforgeryText = await antiforgeryResponse.Content.ReadAsStringAsync(token);
            EnsureSuccess(antiforgeryResponse, antiforgeryText, "issue antiforgery token");
            using var antiforgery = JsonDocument.Parse(antiforgeryText);
            request.Headers.Add("X-CSRF-TOKEN", antiforgery.RootElement.GetProperty("requestToken").GetString());
        }
        using var response = await client.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        EnsureSuccess(response, text, $"{method} {path}");
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.Select(value => value.Split(';', 2)[0]).SingleOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));
            if (cookie is not null)
            {
                client.DefaultRequestHeaders.Remove("Cookie");
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        using var payload = JsonDocument.Parse(text);
        return payload.RootElement.Clone();
    }

    private static string EntityId(JsonElement payload) => payload.GetProperty("fields").GetProperty("EID").GetString()
        ?? throw new InvalidOperationException("正式实体创建响应缺少 EID。");

    private static void EnsureSuccess(HttpResponseMessage response, string text, string operation)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{operation} returned {(int)response.StatusCode}: {text}");
    }
}
