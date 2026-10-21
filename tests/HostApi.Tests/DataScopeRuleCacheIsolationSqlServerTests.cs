using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实多资源隔离、停用子节点、跨资源父/根关联及不可归属整体故障")]
    public async Task RuleCache_ResourceErrorsRespectCompleteAssociationsAndSupportedMaintenance()
    {
        await using var scenario = await RuleScenario.CreateAsync(services =>
            services.AddSingleton<IReadinessEvaluator>(provider => new ReadinessEvaluator(
                [new NamedCacheReadinessContributor(provider.GetServices<IPlatformCacheStatus>())])));
        var child = scenario.Rule with { Id = "Cache340Child", ParentId = scenario.Rule.Id, Name = "停用子节点",
            Enabled = false, Subjects = [new("Cache340ChildSubject", "all", null, 1, new Dictionary<string, string> { ["read"] = "deny" })] };
        var original = scenario.Rule with { Children = [child] };
        var part = PartRule(scenario);
        await scenario.SaveAsync(original, HttpStatusCode.NoContent);
        await scenario.SaveAsync(part, HttpStatusCode.NoContent);
        await AssertPartEditAsync(scenario.ExplanationAsync(), false, part.Id);
        var audit = await scenario.AuditCountAsync();
        var actions = JsonSerializer.Serialize(child.Subjects[0].Actions);
        var condition = JsonSerializer.Serialize(child.Condition, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        const string nullConditionChild = """{"type":"group","field":"","operator":"and","value":null,"children":[null]}""";
        foreach (var damage in new[] { "disabledSubjectJson", "nullConditionChild", "subjectOpinion", "foreignParent", "foreignRoot", "missingParent", "orphanSubject", "unknownResource" })
        {
            await scenario.Fixture.RestartAfterMaintenanceAsync(async provider =>
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                var nodes = db.Set<Dictionary<string, object?>>("DataScopeRuleNode");
                var subjects = db.Set<Dictionary<string, object?>>("DataScopeRuleSubject");
                await nodes.Where(row => EF.Property<string>(row, "EID") == child.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => EF.Property<string>(row, "RootId"), damage == "foreignRoot" ? part.Id : original.Id)
                    .SetProperty(row => EF.Property<string>(row, "ParentId"), damage == "foreignParent" ? part.Id : damage == "missingParent" ? "MissingParent340" : original.Id)
                    .SetProperty(row => EF.Property<string>(row, "ResourceKey"), damage == "unknownResource" ? "unknown.resource340" : original.ResourceKey)
                    .SetProperty(row => EF.Property<string>(row, "ConditionJson"), damage == "nullConditionChild" ? nullConditionChild : condition));
                await subjects.Where(row => EF.Property<string>(row, "EID") == child.Subjects[0].Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => EF.Property<string>(row, "NodeId"), damage == "orphanSubject" ? "MissingNode340" : child.Id)
                    .SetProperty(row => EF.Property<string>(row, "ActionsJson"), damage == "disabledSubjectJson" ? "{" : damage == "subjectOpinion" ? "{\"read\":\"broken\"}" : actions));
            });
            await ReconnectRuleScenarioAsync(scenario);
            var global = damage is "orphanSubject" or "unknownResource";
            using (var ready = await scenario.Admin.GetAsync("/api/health/ready"))
                Assert.Equal(global ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, ready.StatusCode);
            using (var position = await scenario.ListPositionsAsync()) await AssertRuleProblemAsync(position, "Persistence.CacheUnavailable");
            if (global || damage is "foreignParent" or "foreignRoot")
            {
                // 外资源根/父被坏子树引用时两端都不可用，不能把目标资源当作完整健康树。
                using var foreign = await scenario.ExplanationAsync();
                await AssertRuleProblemAsync(foreign, "Persistence.CacheUnavailable");
            }
            else await AssertPartEditAsync(scenario.ExplanationAsync(), false, part.Id);
            Assert.Equal(global ? null : 6L, scenario.Cache.RecordCount);
            if (damage == "disabledSubjectJson")
            {
                // 坏前值不能被正式保存绕过；另一个资源的正常维护不会清除或扩大该错误。
                await scenario.SaveAsync(original, HttpStatusCode.InternalServerError);
                Assert.Equal(audit, await scenario.AuditCountAsync());
                await scenario.SaveAsync(part with { Enabled = false }, HttpStatusCode.NoContent);
                await AssertPartEditAsync(scenario.ExplanationAsync(), true);
                await DeleteRuleAsync(scenario, part.Id);
                await AssertPartEditAsync(scenario.ExplanationAsync(), true);
                using (var stillBad = await scenario.ListPositionsAsync()) await AssertRuleProblemAsync(stillBad, "Persistence.CacheUnavailable");
                await scenario.SaveAsync(part, HttpStatusCode.NoContent);
                await AssertPartEditAsync(scenario.ExplanationAsync(), false, part.Id);
            }
            if (damage == "subjectOpinion")
            {
                // 可完整物化和审计的实际前值继续走原保存入口，错误随配置发布一起清除。
                await scenario.SaveAsync(original, HttpStatusCode.NoContent);
                using (var repaired = await scenario.ListPositionsAsync()) Assert.Equal(HttpStatusCode.OK, repaired.StatusCode);
                Assert.Equal(++audit, await scenario.AuditCountAsync());
                var changes = await FeatureHttpTestRequests.ReadAsync(scenario.Admin, scenario.Http.Audit(),
                    "/api/audit/business-changes?TargetId=" + original.Id + "&EventType=dataScope.rule.changed");
                Assert.Contains(changes.GetProperty("items").EnumerateArray().SelectMany(item => item.GetProperty("Changes").EnumerateArray()),
                    change => change.GetProperty("before").ValueKind == JsonValueKind.Object
                        && change.GetProperty("before").GetProperty("value").GetString()!.Contains("broken", StringComparison.Ordinal));
            }
        }
        // 旧值无法完整审计时，唯一既有维护路径仍是停服修复后重启，不增加修复API。
        await scenario.Fixture.RestartAfterMaintenanceAsync(async provider =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("DataScopeRuleNode")
                .Where(row => EF.Property<string>(row, "EID") == child.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "ResourceKey"), original.ResourceKey));
        });
        await ReconnectRuleScenarioAsync(scenario);
        using (var ready = await scenario.Admin.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using (var position = await scenario.ListPositionsAsync()) Assert.Equal(HttpStatusCode.OK, position.StatusCode);
        await AssertPartEditAsync(scenario.ExplanationAsync(), false, part.Id);
        Assert.Equal(audit, await scenario.AuditCountAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "读取跨整树资源迁移和删除时旧操作保留快照，新操作无旧资源残留")]
    public async Task RuleCache_MultiResourceReadsKeepTheirSnapshotAcrossMigrationAndDeletion()
    {
        var facts = new PausedAllRuleFacts();
        await using var scenario = await RuleScenario.CreateAsync(services =>
        {
            services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IDataScopeIdentityProvider)
                && descriptor.ImplementationType == typeof(AllAuthenticatedDataScopeIdentities)));
            services.AddSingleton<IDataScopeIdentityProvider>(facts);
        });
        var original = scenario.Rule with { Subjects = [new("Cache340MigratingSubject", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })] };
        original = original with { Children = [original with { Id = "Cache340MigratingChild", ParentId = original.Id,
            Name = "迁移子树", Subjects = [] }] };
        await scenario.SaveAsync(original, HttpStatusCode.NoContent);
        facts.PauseNext();
        var previousEdit = scenario.EditAsync("旧资源在途拒绝");
        try
        {
            await facts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await AssertPartEditAsync(scenario.ExplanationAsync(), true);
            var moved = original with { ResourceKey = "part.part", Condition = PartRule(scenario).Condition,
                Children = [original.Children[0] with { ResourceKey = "part.part", Condition = PartRule(scenario).Condition }] };
            await scenario.SaveAsync(moved, HttpStatusCode.NoContent);
            await AssertPartEditAsync(scenario.ExplanationAsync(), false, original.Id);
            facts.Continue();
            using (var oldPosition = await previousEdit) Assert.Equal(HttpStatusCode.Forbidden, oldPosition.StatusCode);
            // 暂停的编辑持有同一对象写锁，释放后再验证旧资源的新编辑，避免资产制造锁等待环。
            using (var newPosition = await scenario.EditAsync("旧资源新操作没有残留")) Assert.Equal(HttpStatusCode.OK, newPosition.StatusCode);

            facts.PauseNext();
            var previousPart = scenario.ExplanationAsync();
            try
            {
                await facts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await DeleteRuleAsync(scenario, original.Id);
                await AssertPartEditAsync(scenario.ExplanationAsync(), true);
                facts.Continue();
                await AssertPartEditAsync(previousPart, false, original.Id);
                using var newPosition = await scenario.EditAsync("两资源均无已删树");
                Assert.Equal(HttpStatusCode.OK, newPosition.StatusCode);
            }
            finally { facts.Continue(); }
        }
        finally { facts.Continue(); }
    }

    private static DataScopeRuleDto PartRule(RuleScenario scenario) => new("Cache340Part", "Cache340Part", null, "其他资源完整规则", true,
        "part.part", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(scenario.PartId)),
        [new("Cache340PartSubject", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })], []);

    private static async Task AssertPartEditAsync(Task<HttpResponseMessage> request, bool allowed, string? source = null)
    {
        using var response = await request;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var edit = body.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("key").GetString() == "edit");
        Assert.Equal(allowed, edit.GetProperty("allowed").GetBoolean());
        if (source is not null) Assert.Contains(edit.GetProperty("sources").EnumerateArray(), entry => entry.GetProperty("id").GetString() == source);
    }

    private static async Task DeleteRuleAsync(RuleScenario scenario, string id)
    {
        using var response = await scenario.Http.SendButtonAsync(scenario.Admin, scenario.SavePoint, HttpMethod.Delete, "/api/authorization/data-scope/rules/" + id);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
