using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    // 只增加测试观察，不暂停提交、不改变HTTP输入；两模型仍共用原来的唯一缓存参与者。
    private static void RegisterRuleNaturalCosts(IServiceCollection services, CostCommands commands)
    {
        services.AddSingleton<RuleNaturalPublicationCosts>();
        services.AddScoped<RuleNaturalPublicationParticipant>();
        foreach (var registration in services.Where(descriptor => descriptor.ServiceType == typeof(PlatformCommitRegistration)).ToArray())
        {
            services.Remove(registration);
            services.AddScoped(provider =>
            {
                var original = (PlatformCommitRegistration)registration.ImplementationFactory!(provider);
                return original.ModelKey is "DataScopeRuleNode" or "DataScopeRuleSubject"
                    ? new(original.ModelKey, provider.GetRequiredService<RuleNaturalPublicationParticipant>()) : original;
            });
        }
        services.AddDbContext<EfPlatformDbContext>((provider, options) =>
            options.AddInterceptors(commands, provider.GetRequiredService<RuleNaturalPublicationCosts>()));
    }

    private sealed record RuleNaturalPublicationSample(double ParticipantBeginMilliseconds, double PrepareMilliseconds,
        long PrepareProcessAllocatedBytes, double BlockToReadReleaseMilliseconds, double? CommitToPublicationMilliseconds,
        double WaitingCacheReadMilliseconds, Guid TransactionId, int MatchedCommits, int IgnoredCommits);

    // 两个真实上下文同时提交，确认外事务事件不会冒充规则保存；本探针不进入正式容量阶段。
    private static async Task VerifyRuleObservationAffinityAsync(BusinessAuditHostFixture fixture)
    {
        await using var owned = fixture.Services.CreateAsyncScope();
        await using var foreign = fixture.Services.CreateAsyncScope();
        var db = owned.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var other = foreign.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var observer = fixture.Services.GetRequiredService<RuleNaturalPublicationCosts>();
        observer.Samples.Clear();
        await using var scope = await owned.ServiceProvider.GetRequiredService<RuleNaturalPublicationParticipant>().BeginAsync(default);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await using var otherTransaction = await other.Database.BeginTransactionAsync();
        await scope.PrepareAsync([], default);
        scope.BlockReads();
        await Task.WhenAll(transaction.CommitAsync(), otherTransaction.CommitAsync());
        await scope.PublishAsync();
        await scope.DisposeAsync();
        var sample = Assert.Single(await Task.WhenAll(observer.Samples.ToArray()));
        Assert.Equal(transaction.TransactionId, sample.TransactionId);
        Assert.Equal(1, sample.MatchedCommits);
        Assert.Equal(1, sample.IgnoredCommits);
        Assert.True(sample.CommitToPublicationMilliseconds >= 0);
    }

    private sealed class RuleNaturalPublicationParticipant(RuleNaturalPublicationCosts observer, EfPlatformDbContext db) : IPlatformCommitParticipant
    {
        public string Name => observer.Name;
        public ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken token) => observer.BeginAsync(db, token);
    }

    // 观察真实提交确认与完整发布；一次诊断缓存读在屏障后开始，不用人工暂停制造等待。
    private sealed class RuleNaturalPublicationCosts(DataScopeRuleCache cache) : DbTransactionInterceptor
    {
        private Scope? _active;
        internal ConcurrentQueue<Task<RuleNaturalPublicationSample>> Samples { get; } = new();
        public string Name => cache.Name;
        internal async ValueTask<IPlatformCommitScope> BeginAsync(EfPlatformDbContext db, CancellationToken token)
        {
            var started = Stopwatch.GetTimestamp();
            var inner = await cache.BeginAsync(token);
            return new Scope(this, db, inner, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        {
            var active = Volatile.Read(ref _active);
            if (active is not null)
            {
                if (ReferenceEquals(active.Context, data.Context) && ReferenceEquals(active.Transaction, transaction)
                    && active.TransactionId == data.TransactionId)
                {
                    Interlocked.CompareExchange(ref active.CommittedAt, Stopwatch.GetTimestamp(), 0);
                    Interlocked.Increment(ref active.MatchedCommits);
                }
                else Interlocked.Increment(ref active.IgnoredCommits);
            }
            return Task.CompletedTask;
        }

        // 此诊断值含等待完成的调度成本；HTTP整体延迟仍由原请求组合另外采集。
        private async Task<double> ObserveReadAsync()
        {
            var started = Stopwatch.GetTimestamp();
            await cache.ReadAsync();
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        private sealed class Scope(RuleNaturalPublicationCosts owner, EfPlatformDbContext db, IPlatformCommitScope inner,
            double beginMilliseconds) : IPlatformCommitScope
        {
            private double _prepareMilliseconds;
            private long _prepareAllocated;
            private long _blockedAt;
            internal long CommittedAt;
            internal int MatchedCommits;
            internal int IgnoredCommits;
            internal EfPlatformDbContext Context => db;
            internal DbTransaction? Transaction;
            internal Guid TransactionId;
            private long _publishedAt;
            private Task<double>? _waitingRead;
            private bool _disposed;
            public async ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
            {
                var allocated = GC.GetTotalAllocatedBytes(false);
                var started = Stopwatch.GetTimestamp();
                await inner.PrepareAsync(changes, token);
                _prepareMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _prepareAllocated = GC.GetTotalAllocatedBytes(false) - allocated;
            }

            public void BlockReads()
            {
                var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Rule observation needs its actual write transaction.");
                Transaction = transaction.GetDbTransaction();
                TransactionId = transaction.TransactionId;
                _blockedAt = Stopwatch.GetTimestamp();
                inner.BlockReads();
                Volatile.Write(ref owner._active, this);
                _waitingRead = owner.ObserveReadAsync();
            }

            public async ValueTask PublishAsync()
            {
                await inner.PublishAsync();
                _publishedAt = Stopwatch.GetTimestamp();
            }

            public void MarkUnavailable() => inner.MarkUnavailable();
            public async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                await inner.DisposeAsync();
                var releasedAt = Stopwatch.GetTimestamp();
                Interlocked.CompareExchange(ref owner._active, null, this);
                if (_waitingRead is null) return;
                // 正式保存不等待诊断读的调度完成；阶段HTTP计时停止后由采集方收齐sample。
                owner.Samples.Enqueue(CompleteSampleAsync(releasedAt));
            }

            private async Task<RuleNaturalPublicationSample> CompleteSampleAsync(long releasedAt)
            {
                var waiting = await _waitingRead!;
                return new(beginMilliseconds, _prepareMilliseconds, _prepareAllocated,
                    Stopwatch.GetElapsedTime(_blockedAt, releasedAt).TotalMilliseconds,
                    CommittedAt > 0 ? Stopwatch.GetElapsedTime(CommittedAt, _publishedAt).TotalMilliseconds : null, waiting,
                    TransactionId, MatchedCommits, IgnoredCommits);
            }
        }
    }
}
