using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实Host初载SQL故障自动恢复、合法空表、离线坏配置及重启完整停用树")]
    public async Task RuleCache_HostStartupSqlFailureAndOfflineMaintenancePreserveReadinessAndCompleteConfiguration()
    {
        var source = new RuleLoadFailure { Reject = true };
        await using var scenario = await RuleScenario.CreateAsync(services =>
        {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(source));
            services.AddSingleton<IReadinessEvaluator>(provider => new ReadinessEvaluator(
                [new NamedCacheReadinessContributor(provider.GetServices<IPlatformCacheStatus>())]));
        });
        using var health = scenario.Fixture.CreateClient();
        using (var live = await health.GetAsync("/api/health")) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var ready = await health.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.True(source.Rejected > 0);
        Assert.Null(scenario.Cache.RecordCount);
        using (var administrator = await FeatureHttpTestRequests.SendAsync(scenario.Admin, scenario.PositionRead, HttpMethod.Get,
            "/api/organization/positions", dataAccess: scenario.Http.ReadDeclaration("organization.position.list", scenario.PositionRead)))
            Assert.Equal(HttpStatusCode.OK, administrator.StatusCode);
        using (var dependent = await scenario.ListPositionsAsync()) await AssertRuleProblemAsync(dependent, "Persistence.CacheUnavailable");
        source.Reject = false;
        await scenario.Cache.Published.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, scenario.Cache.RecordCount);
        using (var ready = await health.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using (var emptyConfiguration = await scenario.ListPositionsAsync()) Assert.Equal(HttpStatusCode.OK, emptyConfiguration.StatusCode);

        var child = scenario.Rule with
        {
            Id = "Cache339DisabledChild", ParentId = scenario.Rule.Id, Name = "停用根下完整主体",
            Subjects = Enumerable.Range(0, 10).Select(index => new DataScopeSubjectDto("Cache339DisabledSubject" + index, "all", null, index,
                new Dictionary<string, string> { ["read"] = "deny" })).ToArray()
        };
        var disabled = scenario.Rule with { Enabled = false, Children = [child] };
        await scenario.SaveAsync(disabled, HttpStatusCode.NoContent);
        var auditCount = await scenario.AuditCountAsync();
        // 损坏与修复均发生在Host停止后；不把运行中直接改表当作受支持的缓存刷新入口。
        await scenario.Fixture.RestartAfterMaintenanceAsync(async provider =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("DataScopeRuleNode")
                .Where(row => EF.Property<string>(row, "EID") == disabled.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "ConditionJson"), "{"));
        });
        await ReconnectRuleScenarioAsync(scenario);
        using (var badReady = await scenario.Admin.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.OK, badReady.StatusCode);
        using (var badResource = await scenario.ListPositionsAsync()) await AssertRuleProblemAsync(badResource, "Persistence.CacheUnavailable");
        using (var otherResource = await scenario.ExplanationAsync()) Assert.Equal(HttpStatusCode.OK, otherResource.StatusCode);
        Assert.Equal(13, scenario.Cache.RecordCount);

        var condition = JsonSerializer.Serialize(disabled.Condition, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await scenario.Fixture.RestartAfterMaintenanceAsync(async provider =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("DataScopeRuleNode")
                .Where(row => EF.Property<string>(row, "EID") == disabled.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "ConditionJson"), condition));
        });
        await ReconnectRuleScenarioAsync(scenario);
        using (var ready = await scenario.Admin.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(13, scenario.Cache.RecordCount);
        var loaded = Assert.Single((await scenario.Cache.ReadAsync()).ForResource("organization.position"));
        Assert.False(loaded.Enabled);
        Assert.Equal(10, Assert.Single(loaded.Children).Subjects.Count);
        Assert.Equal(auditCount, await scenario.AuditCountAsync());
        using (var unaffected = await scenario.ListPositionsAsync())
        {
            Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
            var result = await unaffected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(result.GetProperty("items").EnumerateArray(), row => row.GetProperty("EID").GetString() == scenario.PositionId);
        }
        await scenario.SaveAsync(disabled with { Enabled = true }, HttpStatusCode.NoContent);
        using (var nowDenied = await scenario.ListPositionsAsync())
        {
            Assert.Equal(HttpStatusCode.OK, nowDenied.StatusCode);
            var result = await nowDenied.Content.ReadFromJsonAsync<JsonElement>();
            Assert.DoesNotContain(result.GetProperty("items").EnumerateArray(), row => row.GetProperty("EID").GetString() == scenario.PositionId);
        }
        output.WriteLine(JsonSerializer.Serialize(new { phase = "real-host-sql-reload", records = scenario.Cache.RecordCount,
            cacheMaintenanceMilliseconds = scenario.Cache.LastMaintenanceDuration?.TotalMilliseconds,
            note = "完整初载维护耗时，区别于Host重启总耗时" }));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实人员列表名称投影消费批量可读适配，包装故障不降级为空名称或普通拒绝")]
    public async Task RuleCache_BatchReadableProjectionPropagatesUnavailableThroughTheRealListRoute()
    {
        var projection = new RuleProjectionFailure();
        await using var scenario = await RuleScenario.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(projection)));
        await scenario.Http.CreateButtonAsync(scenario.Admin, scenario.Http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "CACHE339PROJECTION", ENAME = "缓存投影人员", PositionId = scenario.PositionId,
                EmploymentStatus = "Active", IsEnabled = true });
        var source = scenario.Http.ReadDeclaration("organization.person.list", scenario.PersonRead);
        var route = "/api/organization/persons?pageSize=100&fields=EID,ENAME,PositionId,PositionName";
        using (var healthy = await FeatureHttpTestRequests.SendAsync(scenario.Ordinary, scenario.PersonRead, HttpMethod.Get, route, dataAccess: source))
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        projection.Invalidate = async () =>
        {
            scenario.Cache.HoldRecovery();
            await using var scope = await scenario.Cache.BeginAsync(default);
            scope.MarkUnavailable();
        };
        projection.Armed = 1;
        try
        {
            using var failed = await FeatureHttpTestRequests.SendAsync(scenario.Ordinary, scenario.PersonRead, HttpMethod.Get, route, dataAccess: source);
            Assert.True(projection.Triggered);
            await AssertRuleProblemAsync(failed, "Persistence.CacheUnavailable");
            await scenario.Cache.RecoveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            scenario.Cache.ReleaseRecovery();
            await scenario.Cache.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { scenario.Cache.ReleaseRecovery(); }
    }

    private static async Task ReconnectRuleScenarioAsync(RuleScenario scenario)
    {
        scenario.Admin.Dispose(); scenario.Ordinary.Dispose();
        scenario.Admin = await LoginExistingAsync("admin");
        scenario.Ordinary = await LoginExistingAsync("cache339");
        scenario.Http = new(scenario.Fixture);

        async Task<HttpClient> LoginExistingAsync(string code)
        {
            var client = scenario.Fixture.CreateClient();
            try
            {
                await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
                using var login = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = "Audit-test-secure-938!" });
                Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
                await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
                return client;
            }
            catch { client.Dispose(); throw; }
        }
    }

    private sealed class RuleLoadFailure : DbCommandInterceptor
    {
        internal volatile bool Reject;
        internal int Rejected;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Reject && (command.CommandText.Contains("[DataScopeRuleNode]", StringComparison.Ordinal)
                || command.CommandText.Contains("[DataScopeRuleSubject]", StringComparison.Ordinal)))
            {
                Interlocked.Increment(ref Rejected);
                throw new IOException("transient real rule SQL read probe");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RuleProjectionFailure : DbCommandInterceptor
    {
        internal int Armed;
        internal bool Triggered;
        internal Func<Task>? Invalidate;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result,
            CancellationToken token = default)
        {
            if (command.CommandText.Contains("[Person]", StringComparison.Ordinal) && command.CommandText.Contains("OFFSET", StringComparison.Ordinal)
                && Interlocked.Exchange(ref Armed, 0) == 1)
            {
                Triggered = true;
                await Invalidate!();
            }
            return result;
        }
    }
}
