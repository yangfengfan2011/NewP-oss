using System.Collections;
using System.Globalization;
using System.Text.Json;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ICP.Platform.Persistence.EfCore.ProviderTargets;

/// <summary>
/// 只从 EF design-time/provider model 读取 canonical target，不承担 diff、SQL 生成或 apply。
/// </summary>
public sealed class EfProviderTargetSource(
    EfPlatformDbContext dbContext,
    CompiledPersistencePlan plan) : IProviderTargetSource
{
    /// <inheritdoc />
    public ProviderTargetSnapshot Read() => Project(
        dbContext.GetService<IDesignTimeModel>().Model,
        plan);

    internal static ProviderTargetSnapshot Project(IModel providerModel, CompiledPersistencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(providerModel);
        ArgumentNullException.ThrowIfNull(plan);

        var modelByPhysicalKey = plan.Models.ToDictionary(
            static model => PhysicalKey(model.SchemaName, model.TableName),
            StringComparer.OrdinalIgnoreCase);
        var tables = new List<ProviderTableTarget>(plan.Models.Count);

        foreach (var definition in plan.Models.OrderBy(static model => model.LogicalKey, StringComparer.Ordinal))
        {
            var entity = providerModel.FindEntityType(definition.LogicalKey)
                ?? throw new InvalidOperationException($"Provider model is missing '{definition.LogicalKey}'.");
            var schema = entity.GetSchema() ?? "dbo";
            var tableName = entity.GetTableName()
                ?? throw new InvalidOperationException($"Provider model '{definition.LogicalKey}' is not mapped to a table.");
            var storeObject = StoreObjectIdentifier.Table(tableName, schema);
            // SQL Server 的初始 migration 按 EF provider 的物理顺序生成列：主键在前，
            // 其余属性按名称排序。只在 Provider Target 投影边界规范化，避免改变 authoring、
            // DTO 或共享运行时模型的公共字段顺序。
            IEnumerable<ICP.Platform.Modeling.Definitions.FieldDefinition> providerFields =
                plan.Provider is PersistenceProvider.SqlServer
                ? definition.Fields
                    .OrderByDescending(static field => field.IsPrimaryKey)
                    .ThenBy(static field => field.Name, StringComparer.Ordinal)
                : definition.Fields;
            var columns = providerFields
                .Select(field => entity.FindProperty(field.Name)
                    ?? throw new InvalidOperationException($"Provider model is missing '{definition.LogicalKey}.{field.Name}'."))
                // ordinalKey 的派生列是 EF 正式物理模型的一部分，不属于业务字段集合。
                .Concat(definition.Fields.Where(field => field.OrdinalKey).Select(field =>
                    entity.FindProperty("__ordinal_" + field.Name)
                    ?? throw new InvalidOperationException($"Provider ordinal index column is missing for {definition.LogicalKey}.{field.Name}.")))
                .Select(property => ProjectColumn(property, storeObject, plan.Provider))
                .ToArray();
            var primaryKey = entity.FindPrimaryKey()
                ?? throw new InvalidOperationException($"Provider model '{definition.LogicalKey}' has no primary key.");

            tables.Add(new ProviderTableTarget(
                definition.OwnerModuleId,
                schema,
                tableName,
                columns,
                ProjectKey(primaryKey, storeObject),
                entity.GetKeys()
                    .Where(key => key != primaryKey)
                    .Select(key => ProjectKey(key, storeObject))
                    .OrderBy(static key => key.Name, StringComparer.Ordinal)
                    .ToArray(),
                entity.GetForeignKeys()
                    .Select(foreignKey => ProjectForeignKey(foreignKey, storeObject, modelByPhysicalKey))
                    .OrderBy(static foreignKey => foreignKey.Name, StringComparer.Ordinal)
                    .ToArray(),
                entity.GetIndexes()
                    .Select(index => ProjectIndex(index, storeObject, plan.Provider))
                    .OrderBy(static index => index.Name, StringComparer.Ordinal)
                    .ToArray(),
                entity.GetCheckConstraints()
                    .Select(constraint => new ProviderCheckConstraintTarget(
                        constraint.Name ?? throw new InvalidOperationException("Provider check constraint has no database name."),
                        constraint.Sql))
                    .OrderBy(static constraint => constraint.Name, StringComparer.Ordinal)
                    .ToArray()));
        }

        return new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            plan.Provider,
            tables);
    }

    private static ProviderColumnTarget ProjectColumn(
        IProperty property,
        StoreObjectIdentifier storeObject,
        PersistenceProvider provider)
    {
        var defaultValue = property.FindAnnotation(RelationalAnnotationNames.DefaultValue)?.Value;
        var storeClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        var identity = property.FindAnnotation("SqlServer:ValueGenerationStrategy")?.Value?.ToString()
            is "IdentityColumn";
        return new ProviderColumnTarget(
            property.GetColumnName(storeObject)
                ?? throw new InvalidOperationException($"Provider property '{property.Name}' has no column name."),
            property.GetColumnType() ?? property.GetRelationalTypeMapping().StoreType,
            storeClrType.FullName ?? storeClrType.Name,
            property.IsColumnNullable(storeObject),
            property.GetMaxLength(),
            property.GetPrecision(),
            property.GetScale(),
            property.IsUnicode(),
            property.IsFixedLength(),
            property.GetCollation(),
            property.GetComputedColumnSql(storeObject),
            property.GetIsStored(storeObject),
            identity,
            property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate,
            defaultValue is null or DBNull ? null : CanonicalValue(defaultValue),
            property.GetDefaultValueSql(storeObject),
            ProjectProviderAnnotations(property.GetAnnotations(), provider));
    }

    private static ProviderKeyTarget ProjectKey(IReadOnlyKey key, StoreObjectIdentifier storeObject) =>
        new(
            key.GetName()
                ?? throw new InvalidOperationException("Provider key has no database name."),
            key.Properties.Select(property => property.GetColumnName(storeObject)!).ToArray());

    private static ProviderForeignKeyTarget ProjectForeignKey(
        IReadOnlyForeignKey foreignKey,
        StoreObjectIdentifier storeObject,
        IReadOnlyDictionary<string, ICP.Platform.Modeling.Definitions.ModelDefinition> modelByPhysicalKey)
    {
        var principalType = foreignKey.PrincipalEntityType;
        var principalSchema = principalType.GetSchema() ?? "dbo";
        var principalTable = principalType.GetTableName()
            ?? throw new InvalidOperationException("Provider foreign key principal has no table.");
        var principalStoreObject = StoreObjectIdentifier.Table(principalTable, principalSchema);
        var principalOwner = modelByPhysicalKey.TryGetValue(
            PhysicalKey(principalSchema, principalTable),
            out var principalModel)
            ? principalModel.OwnerModuleId
            : throw new InvalidOperationException($"Provider foreign key principal '{principalSchema}.{principalTable}' has no owner.");

        return new ProviderForeignKeyTarget(
            foreignKey.GetConstraintName()
                ?? throw new InvalidOperationException("Provider foreign key has no database name."),
            foreignKey.Properties.Select(property => property.GetColumnName(storeObject)!).ToArray(),
            principalOwner,
            principalSchema,
            principalTable,
            foreignKey.PrincipalKey.Properties
                .Select(property => property.GetColumnName(principalStoreObject)!)
                .ToArray(),
            ProjectReferentialAction(foreignKey.DeleteBehavior));
    }

    private static ProviderIndexTarget ProjectIndex(
        IReadOnlyIndex index,
        StoreObjectIdentifier storeObject,
        PersistenceProvider provider) =>
        new(
            index.GetDatabaseName()
                ?? throw new InvalidOperationException("Provider index has no database name."),
            index.Properties.Select(property => property.GetColumnName(storeObject)!).ToArray(),
            index.IsUnique,
            index.IsDescending,
            index.GetFilter(),
            ProjectProviderAnnotations(index.GetAnnotations(), provider));

    private static ProviderAnnotationTarget[] ProjectProviderAnnotations(
        IEnumerable<IAnnotation> annotations,
        PersistenceProvider provider)
    {
        var prefix = provider switch
        {
            PersistenceProvider.SqlServer => "SqlServer:",
            PersistenceProvider.Oracle => "Oracle:",
            PersistenceProvider.MySql => "MySql:",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
        return annotations
            .Where(annotation => annotation.Name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(static annotation => annotation.Name, StringComparer.Ordinal)
            .Select(annotation => new ProviderAnnotationTarget(annotation.Name, CanonicalValue(annotation.Value)))
            .ToArray();
    }

    internal static string CanonicalValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable values)
        {
            return JsonSerializer.Serialize(values.Cast<object?>().Select(CanonicalValue).ToArray());
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString()!
            : value.ToString()!;
    }

    private static string ProjectReferentialAction(DeleteBehavior deleteBehavior) => deleteBehavior switch
    {
        DeleteBehavior.Cascade => "Cascade",
        DeleteBehavior.Restrict => "Restrict",
        DeleteBehavior.SetNull => "SetNull",
        DeleteBehavior.NoAction or
        DeleteBehavior.ClientCascade or
        DeleteBehavior.ClientNoAction or
        DeleteBehavior.ClientSetNull => "NoAction",
        _ => throw new ArgumentOutOfRangeException(nameof(deleteBehavior), deleteBehavior, null)
    };

    private static string PhysicalKey(string? schema, string table) => $"{schema ?? "dbo"}.{table}";
}
