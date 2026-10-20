using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeExpirySqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Folder期限在scalar、延迟SQL分页、Explain和read依赖共用观察；下次操作重新取时")]
    public async Task FolderExpiry_EqualIsValid_ExpiredPriorityCannotOverride_AndDelayedSqlKeepsObservation()
    {
        var clock = new MutableClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(
            services => services.AddSingleton<TimeProvider>(clock), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        await http.InitializeOrganizationAsync(admin);
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "workspace.spaces.access");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var create = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "workspace.folder.create");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Expiry295", "期限角色", "expiry295", "期限账号", entry, read);
        using var ordinary = actor.Client;
        using var initializedRoot = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, initializedRoot.StatusCode);
        var space = await initializedRoot.Content.ReadFromJsonAsync<JsonElement>();
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new
        {
            position = new { spaceId = space.GetProperty("spaceId").GetString(), rootFolderId = space.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() },
            values = new { ENAME = "期限观察Folder" }
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var ruleSave = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("Expiry295Rule", "Expiry295Rule", null, "到期回到规则拒绝", true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("Expiry295RuleAll", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })], []);
        using (var savedRule = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put, "/api/authorization/data-scope/rules/Expiry295Rule", rule))
            Assert.Equal(HttpStatusCode.NoContent, savedRule.StatusCode);
        var input = new DataScopeObjectDto("workspace.folder", id,
            [new("Expiry295Allow", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }, ExpiresAt: "2026-10-17T12:00:00Z"),
             new("Expiry295ExpiredDeny", "user", actor.AccountId, 10, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" }, ExpiresAt: "2026-10-17T11:59:59Z")]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/workspace.folder/" + id, input))
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        using (var atEquality = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/workspace/folders/" + id))
            Assert.True(atEquality.StatusCode == HttpStatusCode.OK, await atEquality.Content.ReadAsStringAsync());

        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
        try
        {
            var observation = scope.ServiceProvider.GetRequiredService<IDataScopeEvaluationScope>();
            var decisions = scope.ServiceProvider.GetRequiredService<IDataScopeObjectDecisionProvider>();
            var plans = scope.ServiceProvider.GetRequiredService<IDataScopeReadPlanProvider>();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            using (observation.BeginOperation())
            {
                var plan = await plans.GetReadPlanAsync("workspace.folder");
                clock.Now = clock.Now.AddTicks(1);
                var query = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("WorkspaceFolder")).AsNoTracking()
                    .Where(row => EF.Property<string>(row, "EID") == id);
                if (plan.Predicate is not null) query = query.Where(plan.Predicate);
                Assert.Equal(1, await query.CountAsync());
                Assert.Single(await query.OrderBy(row => EF.Property<string>(row, "EID")).Skip(0).Take(1).ToArrayAsync());
                Assert.True(await decisions.IsAllowedAsync("workspace.folder", id, "read"));
                Assert.True(await decisions.IsAllowedAsync("workspace.folder", id, "edit"));
                var explanation = await ((DataScopeReadPlans)plans).ExplainAsync("workspace.folder", id, actor.AccountId, default);
                Assert.True(explanation.Single(action => action.Key == "read").Allowed);
                Assert.True(explanation.Single(action => action.Key == "edit").Allowed);
                Assert.DoesNotContain(explanation.SelectMany(action => action.Sources).SelectMany(source => source.Subjects), subject => subject.Id == "Expiry295ExpiredDeny");
            }
            using (observation.BeginOperation())
            {
                Assert.False(await decisions.IsAllowedAsync("workspace.folder", id, "read"));
                Assert.False(await decisions.IsAllowedAsync("workspace.folder", id, "edit"));
                var explanation = await ((DataScopeReadPlans)plans).ExplainAsync("workspace.folder", id, actor.AccountId, default);
                Assert.False(explanation.Single(action => action.Key == "read").Allowed);
                Assert.False(explanation.Single(action => action.Key == "edit").Allowed);
            }
        }
        finally { accessor.HttpContext = previous; }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实提交后期限推进，Standard CRUD仅交安全成功回执，不沿用写前read")]
    public async Task MutationRead_AfterRealCommitUsesFreshObservation_AndReturnsReceiptWhenExpired()
    {
        var clock = new MutableClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var edit = http.Organization("persons", "edit", "detail.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExpiryWrite295", "期限写后读取", "expiry-write295", "期限写后账号", read, edit);
        using var ordinary = actor.Client;
        var created = await http.CreateButtonAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "EXPIRY-WRITE295", ENAME = "修改前", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var rule = new DataScopeRuleDto("ExpiryWriteRule295", "ExpiryWriteRule295", null, "到期拒绝读取", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("ExpiryWriteAll295", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var savedRule = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
            fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/ExpiryWriteRule295", rule))
            Assert.Equal(HttpStatusCode.NoContent, savedRule.StatusCode);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize"), HttpMethod.Put,
            "/api/authorization/data-scope/objects/organization.person/" + id,
            new DataScopeObjectDto("organization.person", id,
                [new("ExpiryWriteAllow295", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" },
                    ExpiresAt: "2026-10-17T12:00:00Z")])))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var commits = 0;
        fixture.Fault.AfterCommit = () => { commits++; clock.Now = clock.Now.AddTicks(1); };
        try
        {
            using var updated = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
                "/api/organization/persons/" + id, new { ENAME = "已经提交" });
            Assert.True(updated.StatusCode == HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
            Assert.Equal(1, commits);
            Assert.Equal("{\"succeeded\":true}", await updated.Content.ReadAsStringAsync());
        }
        finally { fixture.Fault.AfterCommit = null; }
        var actual = await FeatureHttpTestRequests.ReadAsync(admin, read, "/api/organization/persons/" + id);
        Assert.Equal("已经提交", actual.GetProperty("fields").GetProperty("ENAME").GetString());
        using var noLongerReadable = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/organization/persons/" + id);
        Assert.Equal(HttpStatusCode.NotFound, noLongerReadable.StatusCode);
    }

    private sealed class MutableClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
