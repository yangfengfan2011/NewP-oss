using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
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

    private static DbContextOptions<EfPlatformDbContext> CreateOptions(string databaseName) =>
        new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;

    private static ModelDefinition Definition() =>
        new(
            "TransactionProbe",
            "TransactionProbe",
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
