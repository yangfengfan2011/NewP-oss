using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class EfAuditDateTimeMaterializationTests
{
    [Fact]
    public void OnModelCreating_AuditAndInstantProviderValuesRestoreUtcWithoutChangingOrdinaryDateTime()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(nameof(OnModelCreating_AuditAndInstantProviderValuesRestoreUtcWithoutChangingOrdinaryDateTime))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        using var dbContext = new EfPlatformDbContext(options, [TemporalProbeDefinition()]);
        var entityType = dbContext.Model.FindEntityType("TemporalProbe");

        Assert.NotNull(entityType);
        var auditProperty = entityType!.FindProperty("CreatedAt");
        var nullableAuditProperty = entityType.FindProperty("ArchivedAt");
        var instantProperty = entityType.FindProperty("OccurredAt");
        var ordinaryProperty = entityType.FindProperty("BusinessDateTime");
        Assert.NotNull(auditProperty);
        Assert.NotNull(nullableAuditProperty);
        Assert.NotNull(instantProperty);
        Assert.NotNull(ordinaryProperty);
        Assert.Equal(typeof(DateTime?), nullableAuditProperty!.ClrType);
        Assert.Null(ordinaryProperty!.GetValueConverter());

        var converter = auditProperty!.GetValueConverter();
        var nullableConverter = nullableAuditProperty.GetValueConverter();
        var instantConverter = instantProperty!.GetValueConverter();
        Assert.NotNull(converter);
        Assert.NotNull(nullableConverter);
        Assert.NotNull(instantConverter);
        var providerValue = new DateTime(2026, 8, 27, 10, 30, 0, DateTimeKind.Unspecified);
        var materialized = Assert.IsType<DateTime>(converter!.ConvertFromProvider(providerValue));
        var nullableMaterialized = Assert.IsType<DateTime>(nullableConverter!.ConvertFromProvider(providerValue));
        var instantMaterialized = Assert.IsType<DateTime>(instantConverter!.ConvertFromProvider(providerValue));

        Assert.Equal(providerValue.Ticks, materialized.Ticks);
        Assert.Equal(DateTimeKind.Utc, materialized.Kind);
        Assert.Equal(providerValue.Ticks, nullableMaterialized.Ticks);
        Assert.Equal(DateTimeKind.Utc, nullableMaterialized.Kind);
        Assert.Equal(providerValue.Ticks, instantMaterialized.Ticks);
        Assert.Equal(DateTimeKind.Utc, instantMaterialized.Kind);
    }

    private static ModelDefinition TemporalProbeDefinition() => new(
        "TemporalProbe",
        "TemporalProbe",
        ModelKind.Entity,
        [
            new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("ArchivedAt", "ArchivedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true),
            new FieldDefinition("OccurredAt", "OccurredAt", FieldKind.Scalar, FieldValueType.Instant, "datetime", true, false, false),
            new FieldDefinition("BusinessDateTime", "BusinessDateTime", FieldKind.Scalar, FieldValueType.DateTime, "datetime", true, false, false)
        ],
        []);
}
