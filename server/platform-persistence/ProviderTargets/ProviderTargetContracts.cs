using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Persistence.ProviderTargets;

/// <summary>
/// Provider Target 与迁移过渡摘要的稳定版本。
/// </summary>
public static class ProviderTargetSchemaVersions
{
    /// <summary>Provider Target v1。</summary>
    public const string TargetV1 = "provider-target/v1";

    /// <summary>Provider Transition v1。</summary>
    public const string TransitionV1 = "provider-transition/v1";

    /// <summary>迁移旧对象 descriptor v1。</summary>
    public const string FromDescriptorV1 = "provider-transition-from/v1";
}

/// <summary>
/// EF migration operation 用于携带旧对象精确描述的稳定 annotation。
/// </summary>
public static class ProviderTransitionAnnotationNames
{
    /// <summary>版本化旧对象 descriptor。</summary>
    public const string FromDescriptorV1 = "NewP:ProviderTransition:FromDescriptor/v1";
}

/// <summary>
/// 从当前 provider 模型投影出的只读数据库目标。
/// </summary>
public sealed record ProviderTargetSnapshot(
    string SchemaVersion,
    PersistenceProvider Provider,
    IReadOnlyList<ProviderTableTarget> Tables);

/// <summary>
/// 单个物理表的 provider target。
/// </summary>
public sealed record ProviderTableTarget(
    string OwnerModuleId,
    string Schema,
    string Name,
    IReadOnlyList<ProviderColumnTarget> Columns,
    ProviderKeyTarget? PrimaryKey,
    IReadOnlyList<ProviderKeyTarget> UniqueConstraints,
    IReadOnlyList<ProviderForeignKeyTarget> ForeignKeys,
    IReadOnlyList<ProviderIndexTarget> Indexes,
    IReadOnlyList<ProviderCheckConstraintTarget> CheckConstraints);

/// <summary>
/// 单个物理列的 provider target；业务表单默认值不属于该契约。
/// </summary>
public sealed record ProviderColumnTarget(
    string Name,
    string StoreType,
    string ClrType,
    bool IsNullable,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool? IsUnicode,
    bool? IsFixedLength,
    string? Collation,
    string? ComputedColumnSql,
    bool? IsStored,
    bool IsIdentity,
    bool IsRowVersion,
    string? DefaultValue,
    string? DefaultValueSql,
    IReadOnlyList<ProviderAnnotationTarget> ProviderAnnotations);

/// <summary>
/// 主键或唯一约束的 provider target。
/// </summary>
public sealed record ProviderKeyTarget(string Name, IReadOnlyList<string> Columns);

/// <summary>
/// 外键的 provider target。
/// </summary>
public sealed record ProviderForeignKeyTarget(
    string Name,
    IReadOnlyList<string> Columns,
    string PrincipalOwnerModuleId,
    string PrincipalSchema,
    string PrincipalTable,
    IReadOnlyList<string> PrincipalColumns,
    string OnDelete);

/// <summary>
/// 索引的 provider target。
/// </summary>
public sealed record ProviderIndexTarget(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    IReadOnlyList<bool>? IsDescending,
    string? Filter,
    IReadOnlyList<ProviderAnnotationTarget> ProviderAnnotations);

/// <summary>
/// Check constraint 的 provider target。
/// </summary>
public sealed record ProviderCheckConstraintTarget(string Name, string Sql);

/// <summary>
/// 参与 provider target 比较的 provider annotation。
/// </summary>
public sealed record ProviderAnnotationTarget(string Name, string Value);

/// <summary>
/// 单个 migration 的显式前后 Provider Target 描述。
/// </summary>
public sealed record ProviderTransitionSnapshot(
    string SchemaVersion,
    string ModuleId,
    string MigrationId,
    ProviderTargetSnapshot From,
    ProviderTargetSnapshot To)
{
    /// <summary>
    /// 按 migration operation 顺序保存不改变结构的 Required 字段回填事实。
    /// </summary>
    public IReadOnlyList<ProviderRequiredFieldBackfillFact> RequiredFieldBackfills { get; init; } = [];
    /// <summary>同 owner 关系的受控次序回填事实；空集合保持旧历史字节。</summary>
    public IReadOnlyList<ProviderRelationOrdinalBackfillFact> RelationOrdinalBackfills { get; init; } = [];
}

/// <summary>
/// Provider Transition 中不可变的 Required 字段回填归属、目标与有类型字面量事实。
/// </summary>
public sealed record ProviderRequiredFieldBackfillFact(
    string OwnerModuleId,
    string Schema,
    string Table,
    string Column,
    string ClrType,
    string Literal);

/// <summary>固定父分组、名称及关系主键排名的不可变输入；没有任意查询或更新表达式。</summary>
public sealed record ProviderRelationOrdinalBackfillFact(string OwnerModuleId, string Schema, string Table, string Column,
    string ParentColumn, string LinkIdColumn, string ChildColumn, string TypeColumn, string ChildModelKey,
    string TargetTable, string TargetIdColumn, string TargetNameColumn);

/// <summary>
/// Drop 操作缺少旧 facet 时携带的版本化旧对象描述；仅与 <see cref="ProviderTransitionDescriptorCodec"/> 配套生成。
/// </summary>
public sealed record ProviderTransitionFromDescriptor(
    string SchemaVersion,
    string OwnerModuleId,
    string Kind,
    int? Ordinal,
    ProviderTableTarget? Table,
    ProviderColumnTarget? Column,
    ProviderKeyTarget? Key,
    ProviderForeignKeyTarget? ForeignKey,
    ProviderIndexTarget? Index);

/// <summary>
/// 为 migration operation 生成或读取 canonical 旧对象 descriptor。
/// </summary>
public static class ProviderTransitionDescriptorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>生成旧表 descriptor。</summary>
    public static string EncodeTable(string ownerModuleId, ProviderTableTarget table) =>
        Encode(new(ProviderTargetSchemaVersions.FromDescriptorV1, ownerModuleId, "table", null, table, null, null, null, null));

    /// <summary>生成旧列 descriptor，并保留其旧 ordinal。</summary>
    public static string EncodeColumn(string ownerModuleId, int ordinal, ProviderColumnTarget column) =>
        Encode(new(ProviderTargetSchemaVersions.FromDescriptorV1, ownerModuleId, "column", ordinal, null, column, null, null, null));

    /// <summary>生成旧主键或唯一约束 descriptor。</summary>
    public static string EncodeKey(string ownerModuleId, ProviderKeyTarget key) =>
        Encode(new(ProviderTargetSchemaVersions.FromDescriptorV1, ownerModuleId, "key", null, null, null, key, null, null));

    /// <summary>生成旧外键 descriptor。</summary>
    public static string EncodeForeignKey(string ownerModuleId, ProviderForeignKeyTarget foreignKey) =>
        Encode(new(ProviderTargetSchemaVersions.FromDescriptorV1, ownerModuleId, "foreign-key", null, null, null, null, foreignKey, null));

    /// <summary>生成旧索引 descriptor。</summary>
    public static string EncodeIndex(string ownerModuleId, ProviderIndexTarget index) =>
        Encode(new(ProviderTargetSchemaVersions.FromDescriptorV1, ownerModuleId, "index", null, null, null, null, null, index));

    /// <summary>读取并验证 descriptor 的版本和对象互斥结构。</summary>
    public static ProviderTransitionFromDescriptor Decode(string encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        ProviderTransitionFromDescriptor descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize<ProviderTransitionFromDescriptor>(encoded, JsonOptions)
                ?? throw new InvalidOperationException("Provider transition from descriptor is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Provider transition from descriptor is malformed.", exception);
        }

        if (!string.Equals(
                descriptor.SchemaVersion,
                ProviderTargetSchemaVersions.FromDescriptorV1,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptor.OwnerModuleId)
            || !IsValidShape(descriptor))
        {
            throw new InvalidOperationException("Provider transition from descriptor is invalid.");
        }

        return descriptor;
    }

    private static string Encode(ProviderTransitionFromDescriptor descriptor)
    {
        if (!string.Equals(
                descriptor.SchemaVersion,
                ProviderTargetSchemaVersions.FromDescriptorV1,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptor.OwnerModuleId)
            || !IsValidShape(descriptor))
        {
            throw new InvalidOperationException("Provider transition from descriptor is invalid.");
        }

        return JsonSerializer.Serialize(descriptor, JsonOptions);
    }

    private static bool IsValidShape(ProviderTransitionFromDescriptor descriptor)
    {
        var populated = new object?[]
        {
            descriptor.Table,
            descriptor.Column,
            descriptor.Key,
            descriptor.ForeignKey,
            descriptor.Index
        }.Count(static value => value is not null);
        if (populated != 1)
        {
            return false;
        }

        return descriptor.Kind switch
        {
            "table" => descriptor.Table is not null
                && descriptor.Ordinal is null
                && string.Equals(descriptor.Table.OwnerModuleId, descriptor.OwnerModuleId, StringComparison.Ordinal)
                && IsValidTable(descriptor.Table),
            "column" => descriptor.Column is not null
                && descriptor.Ordinal is >= 0
                && IsValidColumn(descriptor.Column),
            "key" => descriptor.Key is not null
                && descriptor.Ordinal is null
                && IsValidKey(descriptor.Key),
            "foreign-key" => descriptor.ForeignKey is not null
                && descriptor.Ordinal is null
                && IsValidForeignKey(descriptor.ForeignKey),
            "index" => descriptor.Index is not null
                && descriptor.Ordinal is null
                && IsValidIndex(descriptor.Index),
            _ => false
        };
    }

    private static bool IsValidTable(ProviderTableTarget table)
    {
        if (string.IsNullOrWhiteSpace(table.OwnerModuleId)
            || string.IsNullOrWhiteSpace(table.Schema)
            || string.IsNullOrWhiteSpace(table.Name)
            || table.Columns is null
            || !table.Columns.All(IsValidColumn)
            || table.Columns.Select(static column => column.Name).Distinct(StringComparer.Ordinal).Count() != table.Columns.Count
            || (table.PrimaryKey is not null && !IsValidKey(table.PrimaryKey))
            || table.UniqueConstraints is null
            || !table.UniqueConstraints.All(IsValidKey)
            || table.ForeignKeys is null
            || !table.ForeignKeys.All(IsValidForeignKey)
            || table.Indexes is null
            || !table.Indexes.All(IsValidIndex)
            || table.CheckConstraints is null
            || !table.CheckConstraints.All(static constraint =>
                !string.IsNullOrWhiteSpace(constraint.Name)
                && !string.IsNullOrWhiteSpace(constraint.Sql)))
        {
            return false;
        }

        var columnNames = table.Columns
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        return (table.PrimaryKey is null || table.PrimaryKey.Columns.All(columnNames.Contains))
            && table.UniqueConstraints.All(key => key.Columns.All(columnNames.Contains))
            && table.ForeignKeys.All(foreignKey =>
                foreignKey.Columns.Count == foreignKey.PrincipalColumns.Count
                && foreignKey.Columns.All(columnNames.Contains))
            && table.Indexes.All(index => index.Columns.All(columnNames.Contains));
    }

    private static bool IsValidColumn(ProviderColumnTarget column) =>
        !string.IsNullOrWhiteSpace(column.Name)
        && !string.IsNullOrWhiteSpace(column.StoreType)
        && !string.IsNullOrWhiteSpace(column.ClrType)
        && column.ProviderAnnotations is not null
        && column.ProviderAnnotations.All(IsValidAnnotation);

    private static bool IsValidKey(ProviderKeyTarget key) =>
        !string.IsNullOrWhiteSpace(key.Name)
        && key.Columns is { Count: > 0 }
        && key.Columns.All(static column => !string.IsNullOrWhiteSpace(column));

    private static bool IsValidForeignKey(ProviderForeignKeyTarget foreignKey) =>
        !string.IsNullOrWhiteSpace(foreignKey.Name)
        && foreignKey.Columns is { Count: > 0 }
        && foreignKey.Columns.All(static column => !string.IsNullOrWhiteSpace(column))
        && !string.IsNullOrWhiteSpace(foreignKey.PrincipalOwnerModuleId)
        && !string.IsNullOrWhiteSpace(foreignKey.PrincipalSchema)
        && !string.IsNullOrWhiteSpace(foreignKey.PrincipalTable)
        && foreignKey.PrincipalColumns is { Count: > 0 }
        && foreignKey.PrincipalColumns.All(static column => !string.IsNullOrWhiteSpace(column))
        && !string.IsNullOrWhiteSpace(foreignKey.OnDelete);

    private static bool IsValidIndex(ProviderIndexTarget index) =>
        !string.IsNullOrWhiteSpace(index.Name)
        && index.Columns is { Count: > 0 }
        && index.Columns.All(static column => !string.IsNullOrWhiteSpace(column))
        && index.ProviderAnnotations is not null
        && index.ProviderAnnotations.All(IsValidAnnotation);

    private static bool IsValidAnnotation(ProviderAnnotationTarget annotation) =>
        !string.IsNullOrWhiteSpace(annotation.Name)
        && annotation.Value is not null;
}

/// <summary>
/// 提供当前已编译计划的长期只读 Provider Target；该边界不生成或执行 DDL。
/// </summary>
public interface IProviderTargetSource
{
    /// <summary>
    /// 读取当前 provider 的 canonical target。
    /// </summary>
    ProviderTargetSnapshot Read();
}

/// <summary>
/// 计算版本化 Provider Target 与 Transition 内容摘要。
/// </summary>
public static class ProviderTargetDigest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>计算 canonical Provider Target v1 摘要。</summary>
    public static string Compute(ProviderTargetSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!string.Equals(target.SchemaVersion, ProviderTargetSchemaVersions.TargetV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported Provider Target schema version '{target.SchemaVersion}'.");
        }

        var canonical = target with
        {
            Tables = target.Tables
                .OrderBy(static table => table.OwnerModuleId, StringComparer.Ordinal)
                .ThenBy(static table => table.Schema, StringComparer.Ordinal)
                .ThenBy(static table => table.Name, StringComparer.Ordinal)
                .Select(Normalize)
                .ToArray()
        };
        return Hash(JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions));
    }

    /// <summary>计算 canonical Provider Transition v1 摘要。</summary>
    public static string Compute(ProviderTransitionSnapshot transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (!string.Equals(transition.SchemaVersion, ProviderTargetSchemaVersions.TransitionV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported Provider Transition schema version '{transition.SchemaVersion}'.");
        }

        ValidateTargetVersion(transition.From);
        ValidateTargetVersion(transition.To);
        if (transition.From.Provider != transition.To.Provider)
        {
            throw new InvalidOperationException("Provider Transition from/to providers differ.");
        }

        if (transition.RequiredFieldBackfills is null
            || transition.RequiredFieldBackfills.Any(static fact =>
                string.IsNullOrWhiteSpace(fact.OwnerModuleId)
                || string.IsNullOrWhiteSpace(fact.Schema)
                || string.IsNullOrWhiteSpace(fact.Table)
                || string.IsNullOrWhiteSpace(fact.Column)
                || string.IsNullOrWhiteSpace(fact.ClrType)
                || fact.Literal is null))
        {
            throw new InvalidOperationException("Provider Transition contains an invalid required-field backfill fact.");
        }

        if (transition.RelationOrdinalBackfills is null || transition.RelationOrdinalBackfills.Any(fact =>
            new[] { fact.OwnerModuleId, fact.Schema, fact.Table, fact.Column, fact.ParentColumn, fact.LinkIdColumn,
                fact.ChildColumn, fact.TypeColumn, fact.ChildModelKey, fact.TargetTable, fact.TargetIdColumn, fact.TargetNameColumn }.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("Provider Transition contains an invalid relation ordinal backfill fact.");

        var canonical = transition with
        {
            From = Normalize(transition.From),
            To = Normalize(transition.To),
            RequiredFieldBackfills = transition.RequiredFieldBackfills.ToArray(),
            RelationOrdinalBackfills = transition.RelationOrdinalBackfills.ToArray()
        };
        // 无回填的既有 Transition v1 保持原 canonical 字节，新增事实不能重写冻结历史前缀。
        var canonicalBytes = canonical.RelationOrdinalBackfills.Count != 0
            ? JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions)
            : canonical.RequiredFieldBackfills.Count == 0
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                canonical.SchemaVersion,
                canonical.ModuleId,
                canonical.MigrationId,
                canonical.From,
                canonical.To
            }, JsonOptions)
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                canonical.SchemaVersion, canonical.ModuleId, canonical.MigrationId, canonical.From, canonical.To, canonical.RequiredFieldBackfills
            }, JsonOptions);
        return Hash(canonicalBytes);
    }

    private static ProviderTargetSnapshot Normalize(ProviderTargetSnapshot target) => target with
    {
        Tables = target.Tables
            .OrderBy(static table => table.OwnerModuleId, StringComparer.Ordinal)
            .ThenBy(static table => table.Schema, StringComparer.Ordinal)
            .ThenBy(static table => table.Name, StringComparer.Ordinal)
            .Select(Normalize)
            .ToArray()
    };

    private static ProviderTableTarget Normalize(ProviderTableTarget table) => table with
    {
        Columns = table.Columns
            .Select(column => column with
            {
                ProviderAnnotations = column.ProviderAnnotations
                    .OrderBy(static item => item.Name, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray(),
        UniqueConstraints = table.UniqueConstraints.OrderBy(static key => key.Name, StringComparer.Ordinal).ToArray(),
        ForeignKeys = table.ForeignKeys.OrderBy(static key => key.Name, StringComparer.Ordinal).ToArray(),
        Indexes = table.Indexes
            .OrderBy(static index => index.Name, StringComparer.Ordinal)
            .Select(index => index with
            {
                ProviderAnnotations = index.ProviderAnnotations.OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray()
            })
            .ToArray(),
        CheckConstraints = table.CheckConstraints.OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray()
    };

    private static string Hash(byte[] canonicalBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));

    private static void ValidateTargetVersion(ProviderTargetSnapshot target)
    {
        if (!string.Equals(target.SchemaVersion, ProviderTargetSchemaVersions.TargetV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported Provider Target schema version '{target.SchemaVersion}'.");
        }
    }
}
