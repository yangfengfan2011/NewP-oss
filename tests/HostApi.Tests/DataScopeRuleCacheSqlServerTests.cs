using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式管理变更、资源迁移、审计与普通用户两规则表零读取")]
    public async Task RuleCache_ManagementLifecyclePreservesResultsAndEliminatesSteadyRuleSql()
    {
        var commands = new CostCommands();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var people = http.Organization("persons");
        var positions = http.Organization("positions");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "Cache338", "规则缓存", "cache338", "规则缓存账号", people, positions)).Client;
        const string name = "Cache338Blocked";
        await http.CreateButtonAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "CACHE338", ENAME = name, EmploymentStatus = "Active", IsEnabled = true });
        await http.CreateButtonAsync(admin, http.Organization("positions", "create", "list.pageActions"), "/api/organization/positions",
            new { ECODE = "CACHE338", ENAME = name });
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var manage = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var root = new DataScopeRuleDto("Cache338Root", "Cache338Root", null, "缓存生命周期", true, "organization.person",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement(name)),
            [new("Cache338Subject", "all", null, 1, new Dictionary<string, string> { ["read"] = "deny" })], []);
        var permissionRuleQueries = new List<int>();

        await AssertVisibleAsync(true, true);
        await SaveAsync(root);
        await AssertVisibleAsync(false, true);
        await SaveAsync(root with { Enabled = false });
        await AssertVisibleAsync(true, true);
        await SaveAsync(root);
        await AssertVisibleAsync(false, true);
        await SaveAsync(root with { Subjects = [new("Cache338Replacement", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "allow" })] });
        await AssertVisibleAsync(true, true);
        var moved = root with { ResourceKey = "organization.position" };
        await SaveAsync(moved);
        await AssertVisibleAsync(true, false);
        commands.Begin();
        try
        {
            var listed = await FeatureHttpTestRequests.ReadAsync(admin, manage, "/api/authorization/data-scope/rules?page=1");
            Assert.Equal("organization.position", Assert.Single(listed.EnumerateArray()).GetProperty("resourceKey").GetString());
        }
        finally { commands.End(); }
        Assert.Contains(commands.Items, item => item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
            && item.Sql.Contains("OFFSET", StringComparison.Ordinal));
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Delete, "/api/authorization/data-scope/rules/" + root.Id))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertVisibleAsync(true, true);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
            "/api/audit/business-changes?TargetId=" + root.Id + "&EventType=dataScope.rule.changed");
        Assert.Equal(6, audit.GetProperty("items").GetArrayLength());
        var changes = audit.GetProperty("items").EnumerateArray().SelectMany(item => item.GetProperty("Changes").EnumerateArray()).ToArray();
        Assert.Contains(changes, change => change.GetProperty("before").ValueKind == JsonValueKind.Object
            && change.GetProperty("before").GetProperty("value").GetString()!.Contains("Cache338Replacement", StringComparison.Ordinal)
            && change.GetProperty("after").GetProperty("value").GetString()!.Contains("organization.position", StringComparison.Ordinal));
        Assert.All(permissionRuleQueries, count => Assert.Equal(0, count));

        async Task SaveAsync(DataScopeRuleDto value)
        {
            commands.Begin();
            try
            {
                using var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
                    "/api/authorization/data-scope/rules/" + value.Id, value);
                Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
            }
            finally { commands.End(); }
            // 日常维护允许管理前值和校验查询，但不能重新全表装载缓存。
            Assert.DoesNotContain(commands.Items, item => item.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && (item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal) || item.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal))
                && !item.Sql.Contains("WHERE", StringComparison.Ordinal));
        }

        async Task AssertVisibleAsync(bool personVisible, bool positionVisible)
        {
            commands.Begin();
            try
            {
                foreach (var (feature, path, execution, expected) in new[] {
                    (people, "persons", "organization.person.list", personVisible),
                    (positions, "positions", "organization.position.list", positionVisible) })
                {
                    var result = await FeatureHttpTestRequests.ReadAsync(ordinary, feature, "/api/organization/" + path + "?pageSize=100",
                        http.ReadDeclaration(execution, feature));
                    Assert.Equal(expected, result.GetProperty("items").EnumerateArray().Any(row => row.GetProperty("ENAME").GetString() == name));
                }
            }
            finally { commands.End(); }
            permissionRuleQueries.Add(commands.Items.Count(item => item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
                || item.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal)));
            Assert.NotEmpty(commands.Items);
        }
    }
}
