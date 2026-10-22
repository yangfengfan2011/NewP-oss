using System.Net;
using System.Net.Http.Json;
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
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "相同Cookie即时消费角色及对象直接授权变化，规则缓存不固定最终结果，Count/分页仍在SQL过滤")]
    public async Task RuleCache_CurrentRoleAndObjectOverridesRemainDynamicAndFilteringPrecedesPaging()
    {
        var commands = new CostCommands();
        await using var scenario = await RuleScenario.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)));
        const string roleCode = "Cache339Dynamic";
        var extra = await scenario.Http.CreateButtonAsync(scenario.Admin, scenario.Http.Role("create"), "/api/authorization/roles",
            new { Code = roleCode, Name = "即时规则角色" });
        var roleId = extra.GetProperty("id").GetString()!;
        await AssignAsync(["Cache339", roleCode]);
        await scenario.SaveAsync(scenario.Rule with { Subjects = [new("Cache339RoleSubject", "role", roleId, 1,
            new Dictionary<string, string> { ["read"] = "deny" })] }, HttpStatusCode.NoContent);
        Assert.False(await PositionVisibleAsync());
        await AssignAsync(["Cache339"]);
        Assert.True(await PositionVisibleAsync());

        var created = await scenario.Http.CreateButtonAsync(scenario.Admin, scenario.Http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "CACHE339DYNAMIC", ENAME = "即时对象覆盖", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        var before = await PersonCountAsync();
        await scenario.SaveAsync(scenario.Rule with { ResourceKey = "organization.person",
            Condition = new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            Subjects = [new("Cache339ReadDeny", "all", null, 1, new Dictionary<string, string> { ["read"] = "deny" })] }, HttpStatusCode.NoContent);
        Assert.Equal(before - 1, await PersonCountAsync(true));
        var manage = scenario.Fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var binding = OwnedResourceReadHttpRequests.Button(scenario.Fixture, manage, "organization.person.authorize");
        var direct = new DataScopeObjectDto("organization.person", id,
            [new("Cache339Direct", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(scenario.Admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/organization.person/" + id, direct, binding))
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        Assert.Equal(before, await PersonCountAsync(true));
        using (var saved = await FeatureHttpTestRequests.SendAsync(scenario.Admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/organization.person/" + id, direct with { Subjects = [] }, binding))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(before - 1, await PersonCountAsync(true));

        async Task AssignAsync(string[] codes)
        {
            using var response = await scenario.Http.SendButtonAsync(scenario.Admin, scenario.Http.Role("userRoles"), HttpMethod.Put,
                "/api/authorization/users/" + scenario.AccountId + "/roles", new { RoleCodes = codes });
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        async Task<bool> PositionVisibleAsync()
        {
            commands.Begin();
            JsonElement result;
            try
            {
                using var response = await scenario.ListPositionsAsync();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                result = await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            finally { commands.End(); }
            AssertNoRuleReads();
            Assert.Contains(commands.Items, command => command.Sql.Contains("[UserRole]", StringComparison.Ordinal));
            return result.GetProperty("items").EnumerateArray().Any(row => row.GetProperty("EID").GetString() == scenario.PositionId);
        }
        async Task<int> PersonCountAsync(bool filtered = false)
        {
            commands.Begin();
            JsonElement result;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(scenario.Ordinary, scenario.PersonRead, HttpMethod.Get,
                    "/api/organization/persons?page=1&pageSize=1&sortBy=PersonNo&sortDirection=asc",
                    dataAccess: scenario.Http.ReadDeclaration("organization.person.list", scenario.PersonRead));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                result = await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            finally { commands.End(); }
            AssertNoRuleReads();
            Assert.Single(result.GetProperty("items").EnumerateArray());
            Assert.Contains(commands.Items, command => command.Sql.Contains("[Person]", StringComparison.Ordinal)
                && command.Sql.Contains("COUNT", StringComparison.Ordinal) && (!filtered || command.Sql.Contains("WHERE", StringComparison.Ordinal)));
            Assert.Contains(commands.Items, command => command.Sql.Contains("[Person]", StringComparison.Ordinal)
                && command.Sql.Contains("OFFSET", StringComparison.Ordinal) && (!filtered || command.Sql.Contains("WHERE", StringComparison.Ordinal)));
            return result.GetProperty("total").GetInt32();
        }
        void AssertNoRuleReads() => Assert.DoesNotContain(commands.Items, command => command.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
            || command.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal));
    }
}
