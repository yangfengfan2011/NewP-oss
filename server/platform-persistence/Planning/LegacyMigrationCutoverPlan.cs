namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 冻结旧统一迁移流向模块归属迁移流切换时唯一受信的历史事实。
/// </summary>
public sealed record LegacyMigrationCutoverDescriptor(
    string ModuleId,
    PersistenceProvider Provider,
    string InitialFromModelDigest,
    string MigrationId,
    IReadOnlySet<string> DroppedPhysicalTables);

/// <summary>
/// 提供一次性 legacy cutover 的受信编译与执行计划；普通模块首迁移仍必须从零摘要开始。
/// </summary>
public static class LegacyMigrationCutoverPlan
{
    public const string OrganizationInitialFromModelDigest =
        "a0d7b6421f280436e2b3726504c3b64a7eaf04dec7fb967faae2046546b3cb2c";

    private static readonly IReadOnlyList<LegacyMigrationCutoverDescriptor> Descriptors =
    [
        new(
            "Organization",
            PersistenceProvider.SqlServer,
            OrganizationInitialFromModelDigest,
            "20260810102141_Organization_000001_PermanentUserBinding",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dbo.PersonUserLink" })
    ];

    public static LegacyMigrationCutoverDescriptor? Find(
        string moduleId,
        PersistenceProvider provider) =>
        Descriptors.SingleOrDefault(descriptor =>
            string.Equals(descriptor.ModuleId, moduleId, StringComparison.Ordinal)
            && descriptor.Provider == provider);

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
}
