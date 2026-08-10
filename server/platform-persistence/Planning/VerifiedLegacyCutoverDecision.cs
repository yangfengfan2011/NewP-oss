using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 表示编译器从内建模块自有迁移产物中签发的一次性 legacy cutover 决策。
/// </summary>
/// <remarks>
/// 构造器与赋值入口只对受信的计划编译器开放；公共调用方可以消费计划，不能自行授予破坏性迁移权限。
/// </remarks>
public sealed class VerifiedLegacyCutoverDecision
{
    private readonly FrozenSet<string> _droppedPhysicalTables;

    internal VerifiedLegacyCutoverDecision(
        string sourceKind,
        string moduleId,
        PersistenceProvider provider,
        string assemblyPath,
        string manifestPath,
        string bundleChecksum,
        string modelDigest,
        string sequence,
        string migrationId,
        string? previousContentDigest,
        string contentDigest,
        string fromModelDigest,
        string toModelDigest,
        string compatibility,
        IEnumerable<string> droppedPhysicalTables)
    {
        SourceKind = sourceKind;
        ModuleId = moduleId;
        Provider = provider;
        AssemblyPath = Path.GetFullPath(assemblyPath);
        ManifestPath = Path.GetFullPath(manifestPath);
        BundleChecksum = bundleChecksum;
        ModelDigest = modelDigest;
        Sequence = sequence;
        MigrationId = migrationId;
        PreviousContentDigest = previousContentDigest;
        ContentDigest = contentDigest;
        FromModelDigest = fromModelDigest;
        ToModelDigest = toModelDigest;
        Compatibility = compatibility;
        _droppedPhysicalTables = droppedPhysicalTables.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        DecisionDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            SourceKind,
            ModuleId,
            Provider = Provider.ToString(),
            BundleChecksum,
            ModelDigest,
            Sequence,
            MigrationId,
            PreviousContentDigest,
            ContentDigest,
            FromModelDigest,
            ToModelDigest,
            Compatibility,
            DroppedPhysicalTables = _droppedPhysicalTables.Order(StringComparer.OrdinalIgnoreCase).ToArray()
        })));
    }

    public string SourceKind { get; }

    public string ModuleId { get; }

    public PersistenceProvider Provider { get; }

    public string AssemblyPath { get; }

    public string ManifestPath { get; }

    public string BundleChecksum { get; }

    public string ModelDigest { get; }

    public string Sequence { get; }

    public string MigrationId { get; }

    public string? PreviousContentDigest { get; }

    public string ContentDigest { get; }

    public string FromModelDigest { get; }

    public string ToModelDigest { get; }

    public string Compatibility { get; }

    public string DecisionDigest { get; }

    public IReadOnlySet<string> DroppedPhysicalTables => _droppedPhysicalTables;

    internal bool AllowsDrop(
        ModuleMigrationStream stream,
        CompiledMigrationStep step,
        string schemaName,
        string tableName) =>
        Matches(stream, step)
        && _droppedPhysicalTables.Contains($"{schemaName}.{tableName}");

    internal bool Matches(
        ModuleMigrationStream stream,
        CompiledMigrationStep step) =>
        string.Equals(SourceKind, "built-in", StringComparison.Ordinal)
        && string.Equals(ModuleId, stream.ModuleId, StringComparison.Ordinal)
        && Provider == stream.Provider
        && string.Equals(AssemblyPath, Path.GetFullPath(stream.AssemblyPath), StringComparison.OrdinalIgnoreCase)
        && string.Equals(ManifestPath, Path.GetFullPath(stream.ManifestPath), StringComparison.OrdinalIgnoreCase)
        && string.Equals(BundleChecksum, stream.BundleChecksum, StringComparison.Ordinal)
        && string.Equals(ModelDigest, stream.ModelDigest, StringComparison.Ordinal)
        && string.Equals(Sequence, step.Sequence, StringComparison.Ordinal)
        && string.Equals(MigrationId, step.MigrationId, StringComparison.Ordinal)
        && string.Equals(PreviousContentDigest, step.PreviousContentDigest, StringComparison.Ordinal)
        && string.Equals(ContentDigest, step.ContentDigest, StringComparison.Ordinal)
        && string.Equals(FromModelDigest, step.FromModelDigest, StringComparison.Ordinal)
        && string.Equals(ToModelDigest, step.ToModelDigest, StringComparison.Ordinal)
        && string.Equals(Compatibility, step.Compatibility, StringComparison.Ordinal)
        && string.Equals(DecisionDigest, step.LegacyCutoverDigest, StringComparison.Ordinal);
}
