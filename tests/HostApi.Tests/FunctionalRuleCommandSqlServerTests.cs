using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FunctionalRuleCommandSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "规则保存和删除使用实际功能按钮，读取目录独立，缺失、借用和撤权均不写入")]
    public async Task RuleCommands_ValidateButtonsAndPreserveIndependentReadDirectory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IObjectActionAccess, UnexpectedObjectAccess>());
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var read = http.Workbench("authorization.dataScope.read");
        var save = http.Workbench("authorization.dataScope.save");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin,
            "Rule326Manager", "规则管理主体", "rule326-manager", "普通规则管理主体", read, save);
        using var ordinary = actor.Client;
        var rule = new DataScopeRuleDto("Rule326", "Rule326", null, "功能资格规则", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement("Person326")),
            [new("Rule326Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        const string path = "/api/authorization/data-scope/rules/Rule326";
        var button = http.Declaration(save.BehaviorKey, save);

        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, path, rule))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var borrowed = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, path, rule,
            http.Declaration("authorization.role.create", http.Role("create"))))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        Assert.DoesNotContain("Rule326", await ReadRulesAsync());
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, path, rule, button))
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        Assert.Contains("Rule326", await ReadRulesAsync());
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Delete, path))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Rule326", await ReadRulesAsync());

        await http.GrantAsync(admin, actor.RoleId, read);
        using (var revoked = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Delete, path, dataAccess: button))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Contains("Rule326", await ReadRulesAsync());
        await http.GrantAsync(admin, actor.RoleId, read, save);
        using (var deleted = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Delete, path, dataAccess: button))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain("Rule326", await ReadRulesAsync());

        Task<string> ReadRulesAsync() => FeatureHttpTestRequests.ReadTextAsync(ordinary, read, "/api/authorization/data-scope/rules");
    }

    // 只为不应经过的对象动作边界设置失败信号，路由、资格、事务和读取仍使用正式实现。
    private sealed class UnexpectedObjectAccess : IObjectActionAccess
    {
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("FUNCTION_ONLY_RULE_REQUESTED_OBJECT_ACCESS");
    }
}
