using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;
    private readonly IEntityAuditValueProvider _auditValueProvider;
    private readonly HashSet<string> _entityModelKeys;
    private readonly string _modelCacheSignature;

    [ActivatorUtilitiesConstructor]
    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        CompiledPersistencePlan plan,
        IEntityAuditValueProvider auditValueProvider)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(auditValueProvider);
        PersistencePlan = plan;
        _definitions = plan.Models;
        _auditValueProvider = auditValueProvider;
        _entityModelKeys = plan.Models
            .Where(static definition => definition.Kind == ModelKind.Entity)
            .Select(static definition => definition.LogicalKey)
            .ToHashSet(StringComparer.Ordinal);
        // Model Digest v1 是冻结的持久契约，不包含后来新增的全部 provider facet
        //（例如 foreign key）。EF 模型缓存键必须使用独立的运行时签名，以免错用其他计划的 provider model。
        _modelCacheSignature = ComputeRuntimeModelCacheSignature(plan);
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        CompiledPersistencePlan plan)
        : this(
            options,
            plan,
            new EntityAuditValueProvider(new SystemEntityAuditStampProvider(TimeProvider.System)))
    {
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions)
        : this(
            options,
            CreateLegacyPlan(definitions),
            new EntityAuditValueProvider(new SystemEntityAuditStampProvider(TimeProvider.System)))
    {
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions,
        IEntityAuditStampProvider auditStampProvider)
        : this(options, CreateLegacyPlan(definitions), new EntityAuditValueProvider(auditStampProvider))
    {
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions,
        IEntityAuditValueProvider auditValueProvider)
        : this(options, CreateLegacyPlan(definitions), auditValueProvider)
    {
    }

    public CompiledPersistencePlan PersistencePlan { get; }

    public string ModelCacheSignature => _modelCacheSignature;

    /// <summary>
    /// 按唯一声明的技术名称解析 EF shared-type entity；不接受旧 owner/name 别名。
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

        throw new InvalidOperationException($"Persistence model '{modelKey}' is absent from the compiled plan.");
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

    // 在任何 provider 写入前取得并校验可信 stamp，再覆盖四字段以封闭最终存储不变量。
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

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                ApplyAuditValues(entry, _auditValueProvider.ForCreate());
            }
            else
            {
                var createdBy = Convert.ToString(entry.Property("CreatedBy").OriginalValue)
                    ?? throw new EntityAuditStampUnavailableException();
                var createdAt = entry.Property("CreatedAt").OriginalValue is DateTime value
                    ? value
                    : throw new EntityAuditStampUnavailableException();
                ApplyAuditValues(entry, _auditValueProvider.ForUpdate(createdBy, createdAt));
                RestoreCreationAuditValue(entry, "CreatedBy");
                RestoreCreationAuditValue(entry, "CreatedAt");
            }
        }
    }

    private static void ApplyAuditValues(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        EntityAuditValues values)
    {
        SetAuditValue(entry, "CreatedBy", values.CreatedBy);
        SetAuditValue(entry, "CreatedAt", values.CreatedAt);
        SetAuditValue(entry, "UpdatedBy", values.UpdatedBy);
        SetAuditValue(entry, "UpdatedAt", values.UpdatedAt);
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

    private static string ComputeRuntimeModelCacheSignature(CompiledPersistencePlan plan)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            provider = plan.Provider.ToString(),
            models = plan.Models.OrderBy(static model => model.LogicalKey, StringComparer.Ordinal)
        });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
