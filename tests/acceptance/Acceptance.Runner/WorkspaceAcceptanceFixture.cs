using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Acceptance;

internal sealed record WorkspaceAcceptanceActor(string AccountId, string ECode, string Password);

/// <summary>通过正式 Identity、Organization 和 Authorization API 准备指定功能集合的普通账号。</summary>
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
        AcceptanceFeatureCatalog features, string runId, Action<string> registerSecret,
        IReadOnlyList<FeaturePoint> grantedFeatures, CancellationToken token)
    {
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
            new { positions = features.GrantSet(grantedFeatures.ToArray()) }, features.Behavior("Authorization", "authorization.workbench.save").Position, token);
        await SendAsync(administrator, HttpMethod.Put, $"/api/authorization/users/{accountId}/roles",
            new { RoleCodes = new[] { roleCode } }, features.Action(roleEntry, "userRoles", "list.rowActions").Position, token);
        using var ordinary = CreateClient(administrator.BaseAddress!.Port);
        await LoginAndChangePasswordAsync(ordinary, ecode, temporaryCredential, password, token);
        return new(accountId, ecode, password);
    }

    // 只用当前普通主体的正式命令准备个人来源孤立对象；移除的是查询返回的 LID，不是实体 EID。
    internal static async Task<string> PrepareOrphanFolderAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string name, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, entry.Position, token);
        var spaces = await SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, entry.Position, token);
        var personal = spaces.EnumerateArray().Single(space => space.GetProperty("spaceKind").GetString() == "Personal");
        var spaceId = personal.GetProperty("spaceId").GetString()!;
        var rootFolderId = personal.GetProperty("rootFolderId").GetString()!;
        var position = new { spaceId, rootFolderId, linkIds = Array.Empty<string>() };
        var created = await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/content/folders",
            new { position, values = new { ENAME = name } },
            features.Action(entry, "create", "list.pageActions", "folder").Position, token);
        var folderId = created.GetProperty("id").GetString()!;
        var scope = JsonSerializer.Serialize(new { spaceId, rootFolderId, linkIds = "[]" });
        var links = await SendAsync(ordinary, HttpMethod.Get,
            $"/api/links/workspace.content?direction=forward&anchorModelKey=Workspace%2FWorkspaceFolder&anchorId={Uri.EscapeDataString(rootFolderId)}&queryScope={Uri.EscapeDataString(scope)}",
            null, entry.Position, token);
        var link = links.GetProperty("items").EnumerateArray().Single(item =>
            item.GetProperty("associatedObject").GetProperty("id").GetString() == folderId);
        var linkId = link.GetProperty("linkId").GetString()!;
        await SendAsync(ordinary, HttpMethod.Delete, $"/api/workspace/content/references/{Uri.EscapeDataString(linkId)}",
            new { position }, features.Action(entry, "remove", "list.rowActions", "folder").Position, token);
        return folderId;
    }

    // 元数据对象由已授权的普通主体正式创建，实体字段契约沿用所属模块。
    internal static async Task<string> CreateMetadataObjectAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string module, string behavior, string path, string code, string name, CancellationToken token)
    {
        var entry = features.Behavior(module, behavior);
        var result = await SendAsync(ordinary, HttpMethod.Post, path, new { ECODE = code, ENAME = name },
            features.Action(entry, "create", "list.pageActions").Position, token);
        // 受控成功回执不含可引用身份，不能伪造 ID 继续准备。
        return result.GetProperty("fields").GetProperty("EID").GetString()
            ?? throw new InvalidOperationException("正式元数据创建未返回可引用的 EID。");
    }

    // 根内容查询与真实列表使用相同的正式 Link 入口；语义比较只留在 API 层。
    internal static Task<JsonElement> ReadRootContentsAsync(HttpClient client, JsonElement root, string entryPosition, CancellationToken token)
    {
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var scope = JsonSerializer.Serialize(new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = rootId, linkIds = "[]" });
        return SendAsync(client, HttpMethod.Get,
            $"/api/links/workspace.content?direction=forward&anchorModelKey=Workspace%2FWorkspaceFolder&anchorId={Uri.EscapeDataString(rootId)}&queryScope={Uri.EscapeDataString(scope)}&pageSize=20",
            null, entryPosition, token);
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
