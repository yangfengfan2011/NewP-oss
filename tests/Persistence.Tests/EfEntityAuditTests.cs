using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class EfEntityAuditTests
{
    [Fact]
    public async Task SaveChangesAsync_WhenEntityIsAdded_OverridesClientAuditValues()
    {
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var provider = new FakeEntityAuditStampProvider("subject-1", now);
        await using var dbContext = CreateDbContext(nameof(SaveChangesAsync_WhenEntityIsAdded_OverridesClientAuditValues), provider);
        var row = new Dictionary<string, object?>
        {
            ["EID"] = "person-1",
            ["CreatedBy"] = "client-value",
            ["CreatedAt"] = DateTime.UnixEpoch,
            ["UpdatedBy"] = "client-value",
            ["UpdatedAt"] = DateTime.UnixEpoch
        };

        dbContext.Set<Dictionary<string, object?>>("Person").Add(row);
        await dbContext.SaveChangesAsync();

        Assert.Equal("subject-1", row["CreatedBy"]);
        Assert.Equal(now, row["CreatedAt"]);
        Assert.Equal("subject-1", row["UpdatedBy"]);
        Assert.Equal(now, row["UpdatedAt"]);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityIsUpdated_PreservesCreationAuditAndRefreshesUpdateAudit()
    {
        var createdAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(30);
        var provider = new FakeEntityAuditStampProvider("creator", createdAt);
        await using var dbContext = CreateDbContext(nameof(SaveChangesAsync_WhenEntityIsUpdated_PreservesCreationAuditAndRefreshesUpdateAudit), provider);
        var row = new Dictionary<string, object?> { ["EID"] = "person-1" };
        dbContext.Set<Dictionary<string, object?>>("Person").Add(row);
        await dbContext.SaveChangesAsync();

        provider.Set("editor", updatedAt);
        row["CreatedBy"] = "client-change";
        row["CreatedAt"] = DateTime.UnixEpoch;
        row["UpdatedBy"] = "client-change";
        row["UpdatedAt"] = DateTime.UnixEpoch;
        await dbContext.SaveChangesAsync();

        Assert.Equal("creator", row["CreatedBy"]);
        Assert.Equal(createdAt, row["CreatedAt"]);
        Assert.Equal("editor", row["UpdatedBy"]);
        Assert.Equal(updatedAt, row["UpdatedAt"]);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenClockMovesBack_DoesNotSetUpdatedAtBeforeCreatedAt()
    {
        var createdAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var provider = new FakeEntityAuditStampProvider("creator", createdAt);
        await using var dbContext = CreateDbContext(nameof(SaveChangesAsync_WhenClockMovesBack_DoesNotSetUpdatedAtBeforeCreatedAt), provider);
        var row = new Dictionary<string, object?> { ["EID"] = "person-1" };
        dbContext.Set<Dictionary<string, object?>>("Person").Add(row);
        await dbContext.SaveChangesAsync();

        provider.Set("editor", createdAt.AddMinutes(-1));
        row["UpdatedBy"] = "client-change";
        await dbContext.SaveChangesAsync();

        Assert.Equal(createdAt, row["UpdatedAt"]);
    }

    [Fact]
    public async Task SaveChangesAsync_WithinAuditOperationScope_ReusesBehaviorAuditValues()
    {
        var behaviorTime = new DateTime(2026, 9, 18, 9, 30, 0, DateTimeKind.Utc);
        var provider = new CountingEntityAuditStampProvider(
            new EntityAuditStamp("person-creator", behaviorTime),
            new EntityAuditStamp("unexpected-second-owner", behaviorTime.AddMinutes(1)));
        var auditValues = new EntityAuditValueProvider(provider);
        await using var dbContext = CreateDbContext(
            nameof(SaveChangesAsync_WithinAuditOperationScope_ReusesBehaviorAuditValues),
            auditValues);
        var row = new Dictionary<string, object?> { ["EID"] = "person-1" };

        using (auditValues.BeginOperation())
        {
            Assert.Equal("person-creator", auditValues.Current().ActorId);
            Assert.Equal(behaviorTime, auditValues.Current().OccurredAtUtc);
            auditValues.ForCreate().ApplyTo(row);
            dbContext.Set<Dictionary<string, object?>>("Person").Add(row);
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(1, provider.Reads);
        Assert.Equal("person-creator", row["CreatedBy"]);
        Assert.Equal(behaviorTime, row["CreatedAt"]);
        Assert.Equal("person-creator", row["UpdatedBy"]);
        Assert.Equal(behaviorTime, row["UpdatedAt"]);
    }

    [Fact]
    public void BeginOperation_EachTopLevelWriteGetsItsOwnTrustedStamp()
    {
        var firstTime = new DateTime(2026, 9, 18, 9, 30, 0, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(1);
        var provider = new CountingEntityAuditStampProvider(
            new EntityAuditStamp("first-actor", firstTime),
            new EntityAuditStamp("second-actor", secondTime));
        var auditValues = new EntityAuditValueProvider(provider);

        EntityAuditValues first;
        using (auditValues.BeginOperation())
        {
            first = auditValues.ForCreate();
        }

        EntityAuditValues second;
        using (auditValues.BeginOperation())
        {
            second = auditValues.ForCreate();
        }

        Assert.Equal("first-actor", first.CreatedBy);
        Assert.Equal(firstTime, first.CreatedAt);
        Assert.Equal("second-actor", second.CreatedBy);
        Assert.Equal(secondTime, second.CreatedAt);
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public void ForUpdate_WhenSqlTimestampHasUnspecifiedKind_PreservesUtcTicksAndLowerBound()
    {
        var createdAt = new DateTime(2026, 9, 18, 9, 30, 0, DateTimeKind.Unspecified);
        var provider = new EntityAuditValueProvider(new FakeEntityAuditStampProvider(
            "editor",
            DateTime.SpecifyKind(createdAt.AddMinutes(-1), DateTimeKind.Utc)));

        var values = provider.ForUpdate("creator", createdAt);

        Assert.Equal(DateTimeKind.Utc, values.CreatedAt.Kind);
        Assert.Equal(createdAt.Ticks, values.CreatedAt.Ticks);
        Assert.Equal(values.CreatedAt, values.UpdatedAt);
    }

    [Fact]
    public void ForCreate_WhenTrustedSourceIsNotRegistered_FailsWithStableCode()
    {
        var provider = new EntityAuditValueProvider(null);

        var exception = Assert.Throws<EntityAuditStampUnavailableException>(provider.ForCreate);

        Assert.Equal(EntityAuditFailureCodes.StampUnavailable, exception.FailureCode);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAuditOwnerReturnsNoValue_FailsWithStableCodeBeforeStoreWrite()
    {
        var databaseName = nameof(SaveChangesAsync_WhenAuditOwnerReturnsNoValue_FailsWithStableCodeBeforeStoreWrite);
        await using var dbContext = CreateDbContext(databaseName, new NullEntityAuditStampProvider());
        dbContext.Set<Dictionary<string, object?>>("Person").Add(
            new Dictionary<string, object?> { ["EID"] = "person-1" });

        var exception = await Assert.ThrowsAsync<EntityAuditStampUnavailableException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(EntityAuditFailureCodes.StampUnavailable, exception.FailureCode);
        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.Set<Dictionary<string, object?>>("Person").AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAuditOwnerCannotBuildNonEmptyValue_FailsWithStableCodeBeforeStoreWrite()
    {
        var databaseName = nameof(SaveChangesAsync_WhenAuditOwnerCannotBuildNonEmptyValue_FailsWithStableCodeBeforeStoreWrite);
        await using var dbContext = CreateDbContext(databaseName, new MissingActorEntityAuditStampProvider());
        dbContext.Set<Dictionary<string, object?>>("Person").Add(
            new Dictionary<string, object?> { ["EID"] = "person-1" });

        var exception = await Assert.ThrowsAsync<EntityAuditStampUnavailableException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(EntityAuditFailureCodes.StampUnavailable, exception.FailureCode);
        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.Set<Dictionary<string, object?>>("Person").AsNoTracking().ToListAsync());
    }

    private static EfPlatformDbContext CreateDbContext(
        string databaseName,
        IEntityAuditStampProvider auditStampProvider)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(options, [EntityDefinition()], auditStampProvider);
    }

    private static EfPlatformDbContext CreateDbContext(
        string databaseName,
        IEntityAuditValueProvider auditValueProvider)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(options, [EntityDefinition()], auditValueProvider);
    }

    private static ModelDefinition EntityDefinition() => new(
        "Person",
        "Person",
        ModelKind.Entity,
        [
            new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", true, false, true),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true)
        ],
        []);

    private sealed class FakeEntityAuditStampProvider : IEntityAuditStampProvider
    {
        private EntityAuditStamp _stamp;

        public FakeEntityAuditStampProvider(string actorId, DateTime occurredAtUtc)
        {
            _stamp = new EntityAuditStamp(actorId, occurredAtUtc);
        }

        public EntityAuditStamp GetCurrent() => _stamp;

        public void Set(string actorId, DateTime occurredAtUtc)
        {
            _stamp = new EntityAuditStamp(actorId, occurredAtUtc);
        }
    }

    private sealed class NullEntityAuditStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => null!;
    }

    private sealed class MissingActorEntityAuditStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new(" ", DateTime.UtcNow);
    }

    private sealed class CountingEntityAuditStampProvider(params EntityAuditStamp[] stamps) : IEntityAuditStampProvider
    {
        public int Reads { get; private set; }

        public EntityAuditStamp GetCurrent() => stamps[Reads++];
    }
}
