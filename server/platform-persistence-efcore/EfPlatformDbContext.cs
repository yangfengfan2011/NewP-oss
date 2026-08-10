using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;
    private readonly IEntityAuditStampProvider _auditStampProvider;
    private readonly HashSet<string> _entityModelKeys;
    private readonly string _modelCacheSignature;

    [ActivatorUtilitiesConstructor]
    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        CompiledPersistencePlan plan,
        IEntityAuditStampProvider auditStampProvider)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(auditStampProvider);
        PersistencePlan = plan;
        _definitions = plan.Models;
        _auditStampProvider = auditStampProvider;
        _entityModelKeys = plan.Models
            .Where(static definition => definition.Kind == ModelKind.Entity)
            .Select(static definition => definition.LogicalKey)
            .ToHashSet(StringComparer.Ordinal);
        _modelCacheSignature = plan.PlanDigest;
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        CompiledPersistencePlan plan)
        : this(
            options,
            plan,
            new SystemEntityAuditStampProvider(TimeProvider.System))
    {
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions)
        : this(
            options,
            CreateLegacyPlan(definitions),
            new SystemEntityAuditStampProvider(TimeProvider.System))
    {
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions,
        IEntityAuditStampProvider auditStampProvider)
        : this(options, CreateLegacyPlan(definitions), auditStampProvider)
    {
    }

    public CompiledPersistencePlan PersistencePlan { get; }

    public string ModelCacheSignature => _modelCacheSignature;

    /// <summary>
    /// 把 qualified key 或唯一的 legacy 裸模型名解析为 EF shared-type entity key。
    /// </summary>
    public string ResolveModelKey(string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);

        var exact = _definitions.SingleOrDefault(definition =>
            string.Equals(definition.LogicalKey, modelKey, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact.LogicalKey;
        }

        var legacyMatches = _definitions
            .Where(definition => string.Equals(definition.Name, modelKey, StringComparison.Ordinal))
            .ToArray();
        return legacyMatches.Length switch
        {
            1 => legacyMatches[0].LogicalKey,
            0 => throw new InvalidOperationException($"Persistence model '{modelKey}' is absent from the compiled plan."),
            _ => throw new InvalidOperationException($"Persistence model name '{modelKey}' is ambiguous; use moduleId/modelKey.")
        };
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var projector = new EfModelProjector();
        projector.Project(modelBuilder, _definitions);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyEntityAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyEntityAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyEntityAuditFields()
    {
        var entries = ChangeTracker.Entries()
            .Where(entry =>
                _entityModelKeys.Contains(entry.Metadata.Name)
                && entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        var stamp = _auditStampProvider.GetCurrent();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                SetAuditValue(entry, "CreatedBy", stamp.ActorId);
                SetAuditValue(entry, "CreatedAt", stamp.OccurredAtUtc);
            }
            else
            {
                RestoreCreationAuditValue(entry, "CreatedBy");
                RestoreCreationAuditValue(entry, "CreatedAt");
            }

            SetAuditValue(entry, "UpdatedBy", stamp.ActorId);
            SetAuditValue(entry, "UpdatedAt", ResolveUpdatedAt(entry, stamp.OccurredAtUtc));
        }
    }

    private static DateTime ResolveUpdatedAt(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        DateTime occurredAtUtc)
    {
        if (entry.State == EntityState.Modified
            && entry.Metadata.FindProperty("CreatedAt") is not null
            && entry.Property("CreatedAt").OriginalValue is DateTime createdAt
            && occurredAtUtc < createdAt)
        {
            return createdAt;
        }

        return occurredAtUtc;
    }

    private static void SetAuditValue(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string fieldName, object value)
    {
        var property = entry.Metadata.FindProperty(fieldName);
        if (property is not null)
        {
            entry.Property(fieldName).CurrentValue = value;
        }
    }

    private static void RestoreCreationAuditValue(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string fieldName)
    {
        var property = entry.Metadata.FindProperty(fieldName);
        if (property is null)
        {
            return;
        }

        var propertyEntry = entry.Property(fieldName);
        propertyEntry.CurrentValue = propertyEntry.OriginalValue;
        propertyEntry.IsModified = false;
    }

    private static CompiledPersistencePlan CreateLegacyPlan(IReadOnlyList<ModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var digest = PersistenceModelDigest.Compute(definitions);
        return new CompiledPersistencePlan(
            "platform.core-legacy",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            Array.Empty<byte>(),
            definitions,
            Array.Empty<ModuleMigrationStream>());
    }
}
