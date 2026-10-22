using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
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
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实普通用户编辑跨发布时读取前置和动作复用完整快照，新操作取得新规则")]
    public async Task RuleCache_InFlightObjectActionKeepsOneSnapshotWhileNewOperationsObserveTheSave()
    {
        var facts = new PausedAllRuleFacts();
        await using var scenario = await RuleScenario.CreateAsync(services =>
        {
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(IDataScopeIdentityProvider)
                && descriptor.ImplementationType == typeof(AllAuthenticatedDataScopeIdentities));
            services.Remove(original);
            services.AddSingleton<IDataScopeIdentityProvider>(facts);
        });
        await scenario.SaveAsync(scenario.Rule, HttpStatusCode.NoContent);
        facts.PauseNext();
        var inFlight = scenario.EditAsync("在途按完整原规则完成");
        try
        {
            await facts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await scenario.SaveAsync(scenario.Rule with { Subjects = [new("Cache339NewDeny", "all", null, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })] }, HttpStatusCode.NoContent);
            facts.Continue();
            using var previous = await inFlight;
            Assert.Equal(HttpStatusCode.OK, previous.StatusCode);
            using var current = await scenario.EditAsync("新操作按新规则拒绝");
            Assert.Equal(HttpStatusCode.Forbidden, current.StatusCode);
        }
        finally { facts.Continue(); }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式保存提交前已建屏障、提交发布窗口等待读取与独立取消")]
    public async Task RuleCache_RealSaveBlocksNewReadsBeforeCommitAndUntilPublicationAndReleasesCancellation()
    {
        var commit = new RuleBeforeCommitGate();
        RulePublicationGate publication = null!;
        await using var scenario = await RuleScenario.CreateAsync(services =>
        {
            services.AddSingleton<RulePublicationGate>(provider => new(provider.GetRequiredService<ICP.Platform.Modules.Authorization.Caching.DataScopeRuleCache>()));
            // 保留其他模型的正式参与者，仅将两规则模型的现有参与者包上可控发布窗口。
            DecorateRuleCommitRegistrations(services, provider => provider.GetRequiredService<RulePublicationGate>());
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commit));
        });
        publication = scenario.Fixture.Services.GetRequiredService<RulePublicationGate>();
        await scenario.SaveAsync(scenario.Rule, HttpStatusCode.NoContent);
        commit.Armed = true;
        publication.Armed = true;
        var changed = scenario.Rule with { Subjects = [new("Cache339BarrierDeny", "all", null, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })] };
        var save = scenario.SendSaveAsync(changed);
        try
        {
            await commit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var waiting = scenario.Cache.ReadAsync().AsTask();
            Assert.False(waiting.IsCompleted);
            using var cancel = new CancellationTokenSource();
            var cancelled = scenario.Cache.ReadAsync(cancel.Token).AsTask();
            Assert.False(cancelled.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.False(waiting.IsCompleted);
            var waitingAt = Stopwatch.GetTimestamp();
            commit.Release.TrySetResult();
            await publication.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(waiting.IsCompleted);
            publication.Release.TrySetResult();
            using var response = await save;
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var complete = await waiting;
            Assert.Equal("deny", Assert.Single(complete.ForResource("organization.position")).Subjects[0].Actions["edit"]);
            output.WriteLine(JsonSerializer.Serialize(new { phase = "controlled-commit-publication-window",
                commitPublicationMilliseconds = publication.WindowMilliseconds,
                waitingReadMilliseconds = Stopwatch.GetElapsedTime(waitingAt).TotalMilliseconds,
                note = "受控交错观察值，含测试发布暂停，不作为自然生产延迟或通过预算" }));
            using var current = await scenario.EditAsync("发布后新规则");
            Assert.Equal(HttpStatusCode.Forbidden, current.StatusCode);
        }
        finally { commit.Release.TrySetResult(); publication.Release.TrySetResult(); }
    }

    private sealed class PausedAllRuleFacts : IDataScopeIdentityProvider
    {
        private readonly AllAuthenticatedDataScopeIdentities _inner = new();
        private int _pause;
        internal TaskCompletionSource Entered = NewSignal();
        private TaskCompletionSource _release = NewSignal();
        public IReadOnlyList<DataScopeSubjectType> SubjectTypes => _inner.SubjectTypes;
        public IReadOnlyList<DataScopeContextValue> ContextValues => _inner.ContextValues;
        public IReadOnlyList<DataScopeContextBinding> ContextBindings => [];
        internal void PauseNext() { Entered = NewSignal(); _release = NewSignal(); Interlocked.Exchange(ref _pause, 1); }
        internal void Continue() => _release.TrySetResult();
        public async Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token)
        {
            if (Interlocked.Exchange(ref _pause, 0) == 1)
            {
                Entered.TrySetResult();
                await _release.Task.WaitAsync(token);
            }
            return await _inner.ResolveAsync(accountId, token);
        }
        public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token) =>
            _inner.SearchAsync(type, query, page, currentIds, token);
    }

    private sealed class RuleBeforeCommitGate : DbTransactionInterceptor
    {
        internal bool Armed;
        internal TaskCompletionSource Entered = NewSignal();
        internal TaskCompletionSource Release = NewSignal();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        {
            if (Armed)
            {
                Armed = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return result;
        }
    }

    private sealed class RulePublicationGate(IPlatformCommitParticipant inner) : IPlatformCommitParticipant
    {
        public string Name => inner.Name;
        internal bool Armed;
        internal TaskCompletionSource Entered = NewSignal();
        internal TaskCompletionSource Release = NewSignal();
        internal double WindowMilliseconds;
        public async ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken token) => new Scope(this, await inner.BeginAsync(token));

        private sealed class Scope(RulePublicationGate owner, IPlatformCommitScope inner) : IPlatformCommitScope
        {
            private long _blockedAt;
            public ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token) => inner.PrepareAsync(changes, token);
            public void BlockReads() { inner.BlockReads(); _blockedAt = Stopwatch.GetTimestamp(); }
            public async ValueTask PublishAsync()
            {
                if (owner.Armed)
                {
                    owner.Armed = false;
                    owner.Entered.TrySetResult();
                    await owner.Release.Task;
                }
                await inner.PublishAsync();
                owner.WindowMilliseconds = Stopwatch.GetElapsedTime(_blockedAt).TotalMilliseconds;
            }
            public void MarkUnavailable() => inner.MarkUnavailable();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
