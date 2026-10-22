using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeWorkspaceFormalSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式裁剪Host与native包中的Folder可浏览即解释他人，私人对象不能经通用出口旁路")]
    public async Task BrowsableFolder_ExplainsOtherAccountWithoutAuthorize_AndKeepsPrivateBoundary()
    {
        var path = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT")
            ?? throw new InvalidOperationException("正式Folder切片需要已登记的发行输入。");
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var package = input.RootElement.GetProperty("packagePath").GetString()!;
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await EntityOwnershipMigrationSqlServerTests.DeployAsync(owned, "install-native", package);
            await EntityOwnershipMigrationSqlServerTests.DeployAsync(owned, "enable-bundle", package);
        }, sourceHostRoot: input.RootElement.GetProperty("hostRoot").GetString()!);
        var http = new FeatureHttpTestRequests(fixture);
        var admin = fixture.Administrator;
        var entry = fixture.Catalog.Single(point => point.BehaviorKey == "workspace.spaces.access");
        var read = fixture.Catalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var create = fixture.Catalog.Single(point => point.BehaviorKey == "workspace.folder.create");
        await http.InitializeOrganizationAsync(admin);
        var publicRoot = await PostAsync("workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var publicPosition = Position(publicRoot);
        var folder = await SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders",
            new { position = publicPosition, values = new { ENAME = "权限查询公共目录" } }, http.Declaration("workspace.folder.create", create));
        var id = folder.GetProperty("id").GetString()!;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var code = "scope295-" + suffix;
        var roleCode = "Scope295" + suffix;
        var role = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles",
            new { Code = roleCode, Name = "权限只读角色" + suffix });
        var account = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = code, ENAME = "权限只读账号" + suffix });
        var accountId = account.GetProperty("account").GetProperty("eid").GetString()!;
        await HttpTestActors.PrepareOrganizationAsync(admin, http, account.GetProperty("account"));
        using (var assigned = await http.SendButtonAsync(admin, http.Role("userRoles"), HttpMethod.Put,
            $"/api/authorization/users/{accountId}/roles", new { RoleCodes = new[] { roleCode } }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        await http.GrantAsync(admin, role.GetProperty("id").GetString()!, entry, read, create);
        using var ordinary = await fixture.LoginAsync(code, account.GetProperty("temporaryCredential").GetString()!);
        using var before = await ordinary.GetFromJsonAsync<JsonDocument>("/api/auth/current-user");
        using var passwordChange = await ordinary.PostAsJsonAsync("/api/identity/me/password-change", new
        {
            CurrentPassword = account.GetProperty("temporaryCredential").GetString(), NewPassword = "Scope295-secure-938!",
            ExpectedRevision = before!.RootElement.GetProperty("revision").GetInt64()
        });
        Assert.True(passwordChange.IsSuccessStatusCode, await passwordChange.Content.ReadAsStringAsync());
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(ordinary);
        var actor = before.RootElement.GetProperty("id").GetString();
        using var administrator = await admin.GetFromJsonAsync<JsonDocument>("/api/auth/current-user");
        var otherAccountId = administrator!.RootElement.GetProperty("id").GetString()!;
        var current = await FeatureHttpTestRequests.ReadAsync(ordinary, read, $"/api/workspace/folders/{id}");
        Assert.Equal(id, current.GetProperty("id").GetString());

        var scope = JsonSerializer.Serialize(new
        {
            spaceId = publicRoot.GetProperty("spaceId").GetString(), rootFolderId = publicRoot.GetProperty("rootFolderId").GetString(),
            linkIds = "[]"
        });
        var list = await FeatureHttpTestRequests.ReadAsync(ordinary, entry,
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId="
            + publicRoot.GetProperty("rootFolderId").GetString() + "&queryScope=" + Uri.EscapeDataString(scope));
        var linkId = Assert.Single(list.GetProperty("items").EnumerateArray(), row =>
            row.GetProperty("associatedObject").GetProperty("id").GetString() == id).GetProperty("linkId").GetString()!;
        var endpoint = $"/api/authorization/data-scope/objects/workspace.folder/{id}";
        using var explained = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/workspace/content/{linkId}/permissions/explanation?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(publicPosition)) + "&accountId=" + Uri.EscapeDataString(otherAccountId),
            dataAccess: http.ReadDeclaration("workspace.content.authorization.view", read));
        Assert.True(explained.StatusCode == HttpStatusCode.OK, await explained.Content.ReadAsStringAsync());
        using var after = await ordinary.GetFromJsonAsync<JsonDocument>("/api/auth/current-user");
        Assert.Equal(actor, after!.RootElement.GetProperty("id").GetString());
        var manage = fixture.Catalog.First(point => point.BehaviorKey == "workspace.folder.authorize");
        using var unprivilegedSave = await http.SendButtonAsync(ordinary, manage, HttpMethod.Put, endpoint,
            new DataScopeObjectDto("workspace.folder", id, []));
        Assert.Equal(HttpStatusCode.Forbidden, unprivilegedSave.StatusCode);

        var personal = await SendAsync(ordinary, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var privateFolder = await SendAsync(ordinary, create, HttpMethod.Post, "/api/workspace/content/folders",
            new { position = Position(personal), values = new { ENAME = "不能代看的私人目录" } }, http.Declaration("workspace.folder.create", create));
        var privateId = privateFolder.GetProperty("id").GetString()!;
        foreach (var suffixPath in new[] { "", "/eligibility", "/catalog", "/subjects/user", "/explanation" })
        {
            using var denied = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Get,
                $"/api/authorization/data-scope/objects/workspace.folder/{privateId}" + suffixPath);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using var deniedSave = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Put,
            $"/api/authorization/data-scope/objects/workspace.folder/{privateId}", new DataScopeObjectDto("workspace.folder", privateId, []));
        Assert.Equal(HttpStatusCode.NotFound, deniedSave.StatusCode);

        // 管理资格不裁剪为自身业务动作；提交后自行失去read/authorize仍只返回安全成功。
        await http.GrantAsync(admin, role.GetProperty("id").GetString()!, entry, read, create, manage);
        var configuration = new DataScopeObjectDto("workspace.folder", id,
            [new("Scope295All" + suffix, "all", null, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow", ["edit"] = "deny" })]);
        using (var seed = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Put, endpoint, configuration))
            Assert.Equal(HttpStatusCode.NoContent, seed.StatusCode);
        var selfRevocation = configuration with { Subjects =
            [new("Scope295Self" + suffix, "user", accountId, 2,
                new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny", ["edit"] = "allow" })] };
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, manage, HttpMethod.Put, endpoint, selfRevocation))
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        using (var inaccessible = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, manage, HttpMethod.Get, endpoint))
            Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);

        Task<JsonElement> PostAsync(string behavior, string route, object? body = null) => SendAsync(admin,
            fixture.Catalog.First(point => point.BehaviorKey == behavior), HttpMethod.Post, route, body);
        static object Position(JsonElement root) => new
        {
            spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(),
            linkIds = Array.Empty<string>()
        };
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, ICP.Platform.Kernel.ModulePlanning.FeaturePoint point,
        HttpMethod method, string path, object? body = null, ICP.Platform.Kernel.ModulePlanning.ActionDataAccessRequirement? dataAccess = null)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, method, path, body, dataAccess);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
