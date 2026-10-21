using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实保存准备/回滚/发布/未知提交及各正式消费者503与恢复")]
    public async Task RuleCache_CommitFailuresPreserveFactsAndRecoverWithoutReplayingWrites()
    {
        var acknowledgement = new RuleCommitAcknowledgement();
        var commands = new CostCommands();
        await using var scenario = await RuleScenario.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(acknowledgement, commands)));
        var cache = scenario.Cache;
        await scenario.SaveAsync(scenario.Rule, HttpStatusCode.NoContent);
        var originalAudit = await scenario.AuditCountAsync();
        var denied = scenario.Rule with { Subjects = [new("Cache339Deny", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })] };

        cache.RejectPreparation = true;
        try { await scenario.SaveAsync(denied, HttpStatusCode.InternalServerError); }
        finally { cache.RejectPreparation = false; }
        Assert.Equal(originalAudit, await scenario.AuditCountAsync());
        Assert.Equal("allow", await scenario.StoredEditAsync());
        using (var allowed = await scenario.EditAsync("准备失败仍可编辑")) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        scenario.Fixture.Fault.Mode = "commit";
        try { await scenario.SaveAsync(denied, HttpStatusCode.InternalServerError); }
        finally { scenario.Fixture.Fault.Mode = ""; }
        Assert.Equal(originalAudit, await scenario.AuditCountAsync());
        Assert.Equal("allow", await scenario.StoredEditAsync());

        foreach (var unknown in new[] { false, true })
        {
            await scenario.SaveAsync(scenario.Rule, HttpStatusCode.NoContent);
            var before = await scenario.AuditCountAsync();
            cache.HoldRecovery();
            cache.FailPublication = !unknown;
            acknowledgement.Enabled = unknown;
            try
            {
                using var response = await scenario.SendSaveAsync(denied);
                var problem = await AssertRuleProblemAsync(response, unknown ? "Persistence.CommitOutcomeUnknown" : "Persistence.CommittedCacheUnavailable");
                Assert.Equal(!unknown, problem.GetProperty("databaseCommitted").GetBoolean());
                Assert.Equal(unknown, problem.GetProperty("commitOutcomeUnknown").GetBoolean());
                await cache.RecoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal("deny", await scenario.StoredEditAsync());
                Assert.Equal(before + 1, await scenario.AuditCountAsync());
                commands.Begin();
                try
                {
                    using (var list = await scenario.ListPositionsAsync()) await AssertRuleProblemAsync(list, "Persistence.CacheUnavailable");
                    using (var explanation = await scenario.ExplanationAsync()) await AssertRuleProblemAsync(explanation, "Persistence.CacheUnavailable");
                    using (var write = await scenario.EditAsync("不可用时不能写"))
                    {
                        Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
                        var error = await write.Content.ReadFromJsonAsync<JsonElement>();
                        Assert.Equal("Persistence.CacheUnavailable", error.GetProperty("code").GetString());
                        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
                    }
                    using (var blockedSave = await scenario.SendSaveAsync(scenario.Rule))
                        await AssertRuleProblemAsync(blockedSave, "Persistence.CacheUnavailable");
                }
                finally { commands.End(); }
                Assert.DoesNotContain(commands.Items, item => item.Sql.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
                    || item.Sql.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal));
                cache.ReleaseRecovery();
                await cache.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                using (var noLongerAllowed = await scenario.EditAsync("恢复后按已保存拒绝"))
                    Assert.Equal(HttpStatusCode.Forbidden, noLongerAllowed.StatusCode);
                using (var independentRead = await scenario.ExplanationAsync()) Assert.Equal(HttpStatusCode.OK, independentRead.StatusCode);
                Assert.Equal(before + 1, await scenario.AuditCountAsync());
            }
            finally { cache.ReleaseRecovery(); acknowledgement.Enabled = false; cache.FailPublication = false; }
        }
    }

    private static async Task<JsonElement> AssertRuleProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("title").GetString());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    private sealed class RuleScenario : IAsyncDisposable
    {
        internal BusinessAuditHostFixture Fixture = null!;
        internal HttpClient Admin = null!;
        internal HttpClient Ordinary = null!;
        internal FeatureHttpTestRequests Http = null!;
        internal FeaturePoint PositionRead = null!;
        internal FeaturePoint PositionEdit = null!;
        internal FeaturePoint PersonRead = null!;
        internal FeaturePoint PartRead = null!;
        internal FeaturePoint SavePoint = null!;
        internal ActionDataAccessRequirement ExplanationSource = null!;
        internal string PositionId = "";
        internal string PartId = "";
        internal string AccountId = "";
        internal DataScopeRuleDto Rule = null!;
        internal RuleCacheProbe Cache => (RuleCacheProbe)Fixture.Services.GetRequiredService<DataScopeRuleCache>();

        internal static async Task<RuleScenario> CreateAsync(Action<IServiceCollection>? configure = null)
        {
            var result = new RuleScenario();
            try
            {
                result.Fixture = await BusinessAuditHostFixture.CreateAsync(services =>
                {
                    services.AddSingleton<DataScopeRuleCache, RuleCacheProbe>();
                    configure?.Invoke(services);
                }, includeWorkspace: true);
                result.Admin = await result.Fixture.LoginAdministratorAsync();
                result.Http = new(result.Fixture);
                result.PositionRead = result.Http.Organization("positions");
                result.PositionEdit = result.Http.Organization("positions", "edit", "list.rowActions");
                result.PersonRead = result.Http.Organization("persons");
                result.PartRead = result.Fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
                result.SavePoint = result.Fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
                var created = await result.Http.CreateButtonAsync(result.Admin, result.Http.Organization("positions", "create", "list.pageActions"),
                    "/api/organization/positions", new { ECODE = "CACHE339", ENAME = "缓存故障岗位" });
                result.PositionId = BusinessAuditSqlServerHttpApiTests.Id(created);
                var position = await ContainerAsync(result.Fixture, result.Admin);
                result.PartId = (await EntityOwnershipBackfillSqlServerHttpApiTests.CreateAsync(result.Fixture, result.Admin,
                    position, "Part", "parts", "createPart", "Cache339Part")).Id;
                var actor = await HttpTestActors.CreateAuthorizedActorAsync(result.Fixture, result.Admin,
                    "Cache339", "规则故障", "cache339", "规则故障账号", result.PositionRead, result.PositionEdit, result.PersonRead, result.PartRead);
                result.Ordinary = actor.Client;
                result.AccountId = actor.AccountId;
                result.ExplanationSource = result.Fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(source =>
                    source.Execution == "part.part.authorization.view" && source.Provider == "authorization.object.view"
                    && source.ViewKey == "part.part.permissions" && source.Surface == "read" && source.FeaturePositions.Contains(result.PartRead.Position));
                result.Rule = new("Cache339Root", "Cache339Root", null, "缓存故障规则", true, "organization.position",
                    new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(result.PositionId)),
                    [new("Cache339Allow", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" })], []);
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }

        internal Task<HttpResponseMessage> SendSaveAsync(DataScopeRuleDto rule) => Http.SendButtonAsync(Admin, SavePoint, HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + rule.Id, rule);
        internal async Task SaveAsync(DataScopeRuleDto rule, HttpStatusCode expected)
        {
            using var response = await SendSaveAsync(rule);
            Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
        }
        internal Task<HttpResponseMessage> ListPositionsAsync() => FeatureHttpTestRequests.SendAsync(Ordinary, PositionRead, HttpMethod.Get,
            "/api/organization/positions", dataAccess: Http.ReadDeclaration("organization.position.list", PositionRead));
        internal Task<HttpResponseMessage> ExplanationAsync() => FeatureHttpTestRequests.SendAsync(Ordinary, PartRead, HttpMethod.Get,
            "/api/part/parts/" + PartId + "/permissions/explanation", dataAccess: ExplanationSource);
        internal Task<HttpResponseMessage> EditAsync(string name) => Http.SendButtonAsync(Ordinary, PositionEdit, HttpMethod.Put,
            "/api/organization/positions/" + PositionId, new { ENAME = name });
        internal async Task<string?> StoredEditAsync()
        {
            var manage = Fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
            var result = await FeatureHttpTestRequests.ReadAsync(Admin, manage, "/api/authorization/data-scope/rules");
            return Assert.Single(result.EnumerateArray()).GetProperty("subjects")[0].GetProperty("actions").GetProperty("edit").GetString();
        }
        internal async Task<int> AuditCountAsync()
        {
            var result = await FeatureHttpTestRequests.ReadAsync(Admin, Http.Audit(), "/api/audit/business-changes?TargetId=" + Rule.Id + "&EventType=dataScope.rule.changed");
            return result.GetProperty("items").GetArrayLength();
        }
        public async ValueTask DisposeAsync()
        {
            Ordinary?.Dispose(); Admin?.Dispose();
            if (Fixture is not null) await Fixture.DisposeAsync();
        }
    }

    private sealed class RuleCommitAcknowledgement : DbTransactionInterceptor
    {
        internal bool Enabled;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        {
            if (!Enabled) return Task.CompletedTask;
            Enabled = false;
            throw new InvalidOperationException("rule cache committed acknowledgement probe");
        }
    }

    private sealed class RuleCacheProbe(IServiceScopeFactory scopes) : DataScopeRuleCache(scopes)
    {
        internal bool RejectPreparation;
        internal bool FailPublication;
        private bool _holdRecovery;
        private bool _recovering;
        internal TaskCompletionSource RecoveryEntered = NewSignal();
        internal TaskCompletionSource Recovered = NewSignal();
        internal TaskCompletionSource Published = NewSignal();
        private TaskCompletionSource _release = NewSignal();
        internal void HoldRecovery()
        {
            _holdRecovery = true; _recovering = false;
            RecoveryEntered = NewSignal(); Recovered = NewSignal(); _release = NewSignal();
        }
        internal void ReleaseRecovery() { _holdRecovery = false; _release.TrySetResult(); }
        protected override async ValueTask<DataScopeRuleSnapshot> ApplyAsync(DataScopeRuleSnapshot snapshot,
            IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            var result = await base.ApplyAsync(snapshot, changes, token);
            if (RejectPreparation) throw new InvalidOperationException("rule cache preparation probe");
            return result;
        }
        protected override async ValueTask<DataScopeRuleSnapshot> LoadAsync(CancellationToken token)
        {
            await WaitForRecoveryAsync(token);
            return await base.LoadAsync(token);
        }
        protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            await WaitForRecoveryAsync(token);
            return await base.ReloadAsync(changes, token);
        }
        protected override void Publish(DataScopeRuleSnapshot snapshot)
        {
            if (FailPublication) { FailPublication = false; throw new InvalidOperationException("rule cache publication probe"); }
            base.Publish(snapshot);
            Published.TrySetResult();
            if (_recovering) { _recovering = false; Recovered.TrySetResult(); }
        }
        private async Task WaitForRecoveryAsync(CancellationToken token)
        {
            if (!_holdRecovery) return;
            _recovering = true;
            RecoveryEntered.TrySetResult();
            await _release.Task.WaitAsync(token);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
