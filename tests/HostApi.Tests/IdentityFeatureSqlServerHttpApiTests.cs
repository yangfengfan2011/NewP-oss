using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class IdentityFeatureSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "Identity 正式声明、位置和既有 Host 管理资格共同满足")]
    public async Task AccountActions_RequirePositionAndHostGuardAndRejectOppositeStatus()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Identity("create");
        // 745 正式目录已含原有非呈现 authorize；声明采用不增加功能位置。
        Assert.Equal(14, fixture.FeatureCatalog.Count(point => point.ModuleId == "Identity"));
        Assert.Single(fixture.FeatureCatalog.Where(point => point.ModuleId == "Identity"
            && point.BehaviorKey == "identity.account.authorize" && point.ActionScope == "detail.custom"));
        foreach (var key in new[] { "edit", "enable", "disable", "delete", "credentialReset" })
        {
            var list = http.Identity(key);
            var detail = http.Identity(key, "detail.pageActions");
            Assert.NotEqual(list.Position, detail.Position);
            Assert.Equal(list.BehaviorKey, detail.BehaviorKey);
        }
        using var missingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/identity/accounts")
        {
            Content = JsonContent.Create(new { ECODE = "missing", ENAME = "缺位置" })
        };
        missingRequest.Headers.Add("X-NewP-Button", http.Declaration(create.BehaviorKey, create).Id);
        using var missing = await admin.SendAsync(missingRequest);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using var wrong = await SendAsync(admin, http.Identity(), HttpMethod.Post, "/api/identity/accounts", new { ECODE = "wrong", ENAME = "错位置" }, http.Declaration(create.BehaviorKey, create));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var created = await CreateAsync(admin, create, "/api/identity/accounts", new { ECODE = "identity-position", ENAME = "位置账号" }, http.Declaration("identity.account.create", create));
        var account = created.GetProperty("account");
        var id = account.GetProperty("eid").GetString();
        var revision = account.GetProperty("revision").GetInt64();
        foreach (var suffix in new[] { "", "/snapshot", "/security-events" })
        {
            using var read = await SendAsync(admin, http.Identity(), HttpMethod.Get, $"/api/identity/accounts/{id}{suffix}",
                dataAccess: http.Declaration("identity.account.read", http.Identity()));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        using var opposite = await http.SendButtonAsync(admin, http.Identity("disable"), HttpMethod.Put, $"/api/identity/accounts/{id}/status/disable",
            new { TargetStatus = "Enabled", ExpectedRevision = revision, Reason = "反向" });
        Assert.Equal(HttpStatusCode.BadRequest, opposite.StatusCode);
        var problem = await opposite.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REQUEST_VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("targetStatus", out _));
        using var legacy = await SendAsync(admin, http.Identity("disable"), HttpMethod.Put, $"/api/identity/accounts/{id}/status",
            new { TargetStatus = "Disabled", ExpectedRevision = revision, Reason = "旧入口" });
        Assert.Equal(HttpStatusCode.Forbidden, legacy.StatusCode);
        using var disabled = await SendAsync(admin, http.Identity("disable"), HttpMethod.Put, $"/api/identity/accounts/{id}/status/disable",
            new { TargetStatus = "Disabled", ExpectedRevision = revision, Reason = "正式停用" }, http.Declaration("identity.account.disable", http.Identity("disable")));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var noButton = await SendAsync(admin, create, HttpMethod.Post, "/api/identity/accounts", new { ECODE = "no-button", ENAME = "缺按钮" });
        Assert.Equal(HttpStatusCode.BadRequest, noButton.StatusCode);
        using var crossed = await SendAsync(admin, http.Identity("disable"), HttpMethod.Put, $"/api/identity/accounts/{id}/status/disable",
            new { TargetStatus = "Disabled", ExpectedRevision = revision, Reason = "借创建按钮" }, http.Declaration("identity.account.create", create));
        Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture,
            admin,
            "identity-guard",
            "identity-guard",
            "identity-guard",
            "identity-guard",
            create);
        using (actor.Client)
        {
            using var guarded = await new FeatureHttpTestRequests(fixture).SendButtonAsync(actor.Client, create, HttpMethod.Post, "/api/identity/accounts", new { ECODE = "guarded", ENAME = "不应扩大管理权" });
            Assert.Equal(HttpStatusCode.Forbidden, guarded.StatusCode);
        }
    }
}
