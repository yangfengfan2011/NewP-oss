using System.Collections.Frozen;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 提供一次性 legacy cutover 的受信编译与执行计划；普通模块首迁移仍必须从零摘要开始。
/// </summary>
public static class LegacyMigrationCutoverPlan
{
    public const string OrganizationInitialFromModelDigest =
        "a0d7b6421f280436e2b3726504c3b64a7eaf04dec7fb967faae2046546b3cb2c";

    private static readonly IReadOnlyList<Descriptor> Descriptors =
    [
        new(
            "Organization",
            PersistenceProvider.SqlServer,
            OrganizationInitialFromModelDigest,
            "20260810102141_Organization_000001_PermanentUserBinding",
            new[] { "dbo.PersonUserLink" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase))
    ];

    /// <summary>
    /// 判断首迁移是否完整匹配受信的内建 legacy cutover 事实。
    /// </summary>
    public static bool IsTrustedInitialMigration(
        string moduleId,
        PersistenceProvider provider,
        string sourceKind,
        string migrationId,
        string compatibility,
        string fromModelDigest)
    {
        var descriptor = Find(moduleId, provider);
        return descriptor is not null
            && string.Equals(sourceKind, "built-in", StringComparison.Ordinal)
            && string.Equals(migrationId, descriptor.MigrationId, StringComparison.Ordinal)
            && string.Equals(compatibility, "backup-restore-only", StringComparison.Ordinal)
            && string.Equals(fromModelDigest, descriptor.InitialFromModelDigest, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断指定迁移是否获准删除冻结白名单中的 legacy 物理表。
    /// </summary>
    public static bool AllowsDrop(
        string moduleId,
        PersistenceProvider provider,
        string migrationId,
        string schemaName,
        string tableName)
    {
        var descriptor = Find(moduleId, provider);
        return descriptor is not null
            && string.Equals(descriptor.MigrationId, migrationId, StringComparison.Ordinal)
            && descriptor.DroppedPhysicalTables.Contains($"{schemaName}.{tableName}");
    }

    private static Descriptor? Find(
        string moduleId,
        PersistenceProvider provider) =>
        Descriptors.SingleOrDefault(descriptor =>
            string.Equals(descriptor.ModuleId, moduleId, StringComparison.Ordinal)
            && descriptor.Provider == provider);

    private sealed record Descriptor(
        string ModuleId,
        PersistenceProvider Provider,
        string InitialFromModelDigest,
        string MigrationId,
        FrozenSet<string> DroppedPhysicalTables);
}
