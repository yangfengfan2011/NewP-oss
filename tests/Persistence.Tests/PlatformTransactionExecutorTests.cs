using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PlatformTransactionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CommitsAllRegisteredChangesThroughOneSharedContext()
    {
        var options = CreateOptions(nameof(ExecuteAsync_CommitsAllRegisteredChangesThroughOneSharedContext));
        await using var dbContext = new EfPlatformDbContext(options, [Definition()]);
        IPlatformTransactionExecutor executor = new EfPlatformTransactionExecutor(dbContext);

        var result = await executor.ExecuteAsync(
            _ =>
            {
                var rows = dbContext.Set<Dictionary<string, object?>>("platform.core/TransactionProbe");
                rows.Add(new Dictionary<string, object?> { ["EID"] = "probe-1" });
                rows.Add(new Dictionary<string, object?> { ["EID"] = "probe-2" });
                return ValueTask.FromResult("committed");
            });

        Assert.Equal("committed", result);
        await using var verification = new EfPlatformDbContext(options, [Definition()]);
        Assert.Equal(2, await verification.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenParticipantFails_RollsBackEveryRegisteredChange()
    {
        var options = CreateOptions(nameof(ExecuteAsync_WhenParticipantFails_RollsBackEveryRegisteredChange));
        await using var dbContext = new EfPlatformDbContext(options, [Definition()]);
        IPlatformTransactionExecutor executor = new EfPlatformTransactionExecutor(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<string>(
            _ =>
            {
                dbContext.Set<Dictionary<string, object?>>("platform.core/TransactionProbe")
                    .Add(new Dictionary<string, object?> { ["EID"] = "probe-failed" });
                throw new InvalidOperationException("participant failed");
            }).AsTask());

        await using var verification = new EfPlatformDbContext(options, [Definition()]);
        Assert.Empty(await verification.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenOuterCallerCatchesNestedFailure_RemainsRollbackOnly()
    {
        var options = CreateOptions(nameof(ExecuteAsync_WhenOuterCallerCatchesNestedFailure_RemainsRollbackOnly));
        await using var dbContext = new EfPlatformDbContext(options, [Definition()]);
        IPlatformTransactionExecutor executor = new EfPlatformTransactionExecutor(dbContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<string>(
            async token =>
            {
                dbContext.Set<Dictionary<string, object?>>("platform.core/TransactionProbe")
                    .Add(new Dictionary<string, object?> { ["EID"] = "outer" });
                try
                {
                    await executor.ExecuteAsync<string>(_ =>
                    {
                        throw new InvalidOperationException("nested failure");
                    }, token);
                }
                catch (InvalidOperationException)
                {
                }
                return "ignored";
            }).AsTask());

        Assert.Equal("nested failure", exception.Message);
        await using var verification = new EfPlatformDbContext(options, [Definition()]);
        Assert.Empty(await verification.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_PersistsChangesRegisteredDuringPreparationBeforePublishing()
    {
        var options = CreateOptions(nameof(ExecuteAsync_PersistsChangesRegisteredDuringPreparationBeforePublishing));
        var definitions = new[] { Definition(), Definition("DeferredProbe") };
        await using var db = new EfPlatformDbContext(options, definitions);
        var participant = new PreparingParticipant(_ =>
        {
            // 模拟提交参与者登记另一上下文的tracked变更；只有平台执行器负责保存。
            db.Set<Dictionary<string, object?>>("platform.core/DeferredProbe").Add(new() { ["EID"] = "deferred" });
            return ValueTask.CompletedTask;
        }, () => Assert.True(db.Set<Dictionary<string, object?>>("platform.core/DeferredProbe").AsNoTracking().Any()));
        var executor = new EfPlatformTransactionExecutor(db, [new("platform.core/TransactionProbe", participant)]);
        await executor.ExecuteAsync(["TransactionProbe"], _ =>
        {
            db.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").Add(new() { ["EID"] = "business" });
            return ValueTask.FromResult(true);
        });
        await using var verification = new EfPlatformDbContext(options, definitions);
        Assert.Single(await verification.Set<Dictionary<string, object?>>("platform.core/DeferredProbe").ToListAsync());
        Assert.Equal(1, participant.PublishCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_RejectsCacheModelChangesDuringPreparationWithoutPublishing(bool explicitRegistration)
    {
        var options = CreateOptions($"{nameof(ExecuteAsync_RejectsCacheModelChangesDuringPreparationWithoutPublishing)}-{explicitRegistration}");
        await using var db = new EfPlatformDbContext(options, [Definition()]);
        EfPlatformTransactionExecutor? executor = null;
        var participant = new PreparingParticipant(_ =>
        {
            var row = new Dictionary<string, object?> { ["EID"] = "too-late" };
            if (explicitRegistration) executor!.RegisterChange(new("TransactionProbe", "too-late", null, row));
            else db.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").Add(row);
            return ValueTask.CompletedTask;
        });
        executor = new EfPlatformTransactionExecutor(db, [new("platform.core/TransactionProbe", participant)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(["TransactionProbe"],
            _ => ValueTask.FromResult(true)).AsTask());
        Assert.Equal(0, participant.PublishCount);
        // InMemory不证明数据库事务回滚；这里证明最后保存之前拒绝缓存模型新变更。
        await using var verification = new EfPlatformDbContext(options, [Definition()]);
        Assert.Empty(await verification.Set<Dictionary<string, object?>>("platform.core/TransactionProbe").ToListAsync());
    }

    private sealed class PreparingParticipant(Func<CancellationToken, ValueTask> prepare, Action? published = null) : IPlatformCommitParticipant, IPlatformCommitScope
    {
        public string Name => "preparation-probe";
        public int PublishCount { get; private set; }
        public ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IPlatformCommitScope>(this);
        public ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken) => prepare(cancellationToken);
        public void BlockReads() { }
        public ValueTask PublishAsync() { published?.Invoke(); PublishCount++; return ValueTask.CompletedTask; }
        public void MarkUnavailable() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DbContextOptions<EfPlatformDbContext> CreateOptions(string databaseName) =>
        new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;

    private static ModelDefinition Definition(string name = "TransactionProbe") =>
        new(
            name,
            name,
            ModelKind.Entity,
            [
                new FieldDefinition(
                    "EID",
                    "EID",
                    FieldKind.Scalar,
                    FieldValueType.ObjectId,
                    "string",
                    Required: true,
                    IsPrimaryKey: true,
                    IsAuditField: false)
            ],
            []);
}
