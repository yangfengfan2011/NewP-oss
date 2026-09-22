using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "动作authoring重装配保留规则、关闭自身过滤并仍完整校验保存")]
    public async Task ReadAction_RestartUsesPreservedConfiguration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Switch", "开关", "scope-switch", "开关主体", read);
        actor.Client.Dispose();
        var created = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SWITCH", ENAME = "Switch", EmploymentStatus = "Active", IsEnabled = true });
        var target = BusinessAuditSqlServerHttpApiTests.Id(created);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var rule = new DataScopeRuleDto("Scope_Switch", "Scope_Switch", null, "读取开关", true, "organization.person",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("Switch")),
            [new("Subject_Switch", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Switch", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var enabled in new[] { false, true })
        {
            await fixture.RestartWithReadActionAsync(enabled);
            using var ordinary = await LoginCurrentAsync("scope-switch");
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{target}"))
                Assert.Equal(enabled ? HttpStatusCode.NotFound : HttpStatusCode.OK, response.StatusCode);
            using var administrator = await LoginCurrentAsync("admin");
            var stored = await FeatureHttpTestRequests.ReadAsync(administrator, entry, "/api/authorization/data-scope/rules");
            Assert.Equal("deny", stored[0].GetProperty("subjects")[0].GetProperty("actions").GetProperty("read").GetString());
            using (var response = await FeatureHttpTestRequests.SendAsync(administrator, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/Scope_Switch", rule with { Condition = rule.Condition with { Operator = "invalid" } }))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        async Task<HttpClient> LoginCurrentAsync(string code)
        {
            var client = fixture.CreateClient();
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = "Audit-test-secure-938!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return client;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实人员读取过滤、事务审计和同 Cookie 配置生效")]
    public async Task PersonRead_RuleSaveFiltersBeforePagingAndRollsBackFailures()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var catalog = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/catalog");
        var resourceKeys = catalog.GetProperty("resources").EnumerateArray().Select(item => item.GetProperty("resource").GetProperty("resourceKey").GetString()).ToArray();
        Assert.Contains("identity.account", resourceKeys);
        using (var scope = fixture.Services.CreateScope())
            Assert.Equal(scope.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>().GetAll().Select(item => item.ResourceKey).Order(), resourceKeys.Order());
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ScopeReader", "范围读取", "scope-reader", "范围读取者", people);
        using var ordinary = actor.Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        var hidden = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons", new { PersonNo = "SCOPE-HIDDEN", ENAME = "Scope Hidden", EmploymentStatus = "Active", IsEnabled = true });
        var visible = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons", new { PersonNo = "SCOPE-VISIBLE", ENAME = "Scope Visible", EmploymentStatus = "Active", IsEnabled = true });
        var hiddenId = BusinessAuditSqlServerHttpApiTests.Id(hidden);
        var visibleId = BusinessAuditSqlServerHttpApiTests.Id(visible);
        var before = await FeatureHttpTestRequests.ReadAsync(ordinary, people, "/api/organization/persons?page=1&pageSize=1");
        var id = "DataScopeRule_Test";
        var rule = new DataScopeRuleDto(id, id, null, "隐藏人员", true, "organization.person",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("Scope Hidden")),
            [new("DataScopeSubject_Test", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, $"/api/authorization/data-scope/rules/{id}", rule))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, $"/api/authorization/data-scope/rules/{id}", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}&EventType=dataScope.rule.changed");
        Assert.Single(audit.GetProperty("items").EnumerateArray());
        // SQL Server 默认排序规则可能把大小写变体解析到同一行，不能因此写坏根身份。
        var alias = rule with { Id = id.ToLowerInvariant(), RootId = id.ToLowerInvariant() };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, $"/api/authorization/data-scope/rules/{alias.Id}", alias))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await FeatureHttpTestRequests.ReadAsync(ordinary, people, "/api/organization/persons?page=1&pageSize=1");
        Assert.Equal(before.GetProperty("total").GetInt32() - 1, after.GetProperty("total").GetInt32());
        Assert.Single(after.GetProperty("items").EnumerateArray());
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{hiddenId}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{visibleId}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, people, HttpMethod.Get, $"/api/organization/persons/{hiddenId}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, $"/api/authorization/data-scope/rules/{id}", rule with { Condition = rule.Condition with { Operator = "unknown" } }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("DataScope.InvalidConfiguration", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        foreach (var mode in new[] { "register", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            try
            {
                using var failed = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
                    $"/api/authorization/data-scope/rules/{id}", rule with { Enabled = false });
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            finally { fixture.Fault.Mode = ""; }
            var kept = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
            Assert.True(kept[0].GetProperty("enabled").GetBoolean());
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, $"/api/authorization/data-scope/rules/{id}", rule with { Enabled = false }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{hiddenId}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 故障注入仅制造持久化配置损坏，读取必须报告异常，LocalAdmin 不执行该条件。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeRuleNode"))
                .Where(row => EF.Property<string>(row, "EID") == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<bool>(row, "Enabled"), true)
                    .SetProperty(row => EF.Property<string>(row, "ConditionJson"), "{}"));
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, "/api/organization/persons"))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, people, HttpMethod.Get, $"/api/organization/persons/{hiddenId}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
