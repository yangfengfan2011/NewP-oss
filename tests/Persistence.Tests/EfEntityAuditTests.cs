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

        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(row);
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
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(row);
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
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(row);
        await dbContext.SaveChangesAsync();

        provider.Set("editor", createdAt.AddMinutes(-1));
        row["UpdatedBy"] = "client-change";
        await dbContext.SaveChangesAsync();

        Assert.Equal(createdAt, row["UpdatedAt"]);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAuditOwnerReturnsNoValue_FailsWithStableCodeBeforeStoreWrite()
    {
        var databaseName = nameof(SaveChangesAsync_WhenAuditOwnerReturnsNoValue_FailsWithStableCodeBeforeStoreWrite);
        await using var dbContext = CreateDbContext(databaseName, new NullEntityAuditStampProvider());
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(
            new Dictionary<string, object?> { ["EID"] = "person-1" });

        var exception = await Assert.ThrowsAsync<EntityAuditStampUnavailableException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(EntityAuditFailureCodes.StampUnavailable, exception.FailureCode);
        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.Set<Dictionary<string, object?>>("platform.core/Person").AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAuditOwnerCannotBuildNonEmptyValue_FailsWithStableCodeBeforeStoreWrite()
    {
        var databaseName = nameof(SaveChangesAsync_WhenAuditOwnerCannotBuildNonEmptyValue_FailsWithStableCodeBeforeStoreWrite);
        await using var dbContext = CreateDbContext(databaseName, new MissingActorEntityAuditStampProvider());
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(
            new Dictionary<string, object?> { ["EID"] = "person-1" });

        var exception = await Assert.ThrowsAsync<EntityAuditStampUnavailableException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(EntityAuditFailureCodes.StampUnavailable, exception.FailureCode);
        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.Set<Dictionary<string, object?>>("platform.core/Person").AsNoTracking().ToListAsync());
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
}
