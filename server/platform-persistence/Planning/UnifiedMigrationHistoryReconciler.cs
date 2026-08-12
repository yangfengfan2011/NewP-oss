using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Persistence.ProviderTargets;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 统一迁移 history reconciliation 的稳定失败码。
/// </summary>
public static class PersistenceHistoryFailureCodes
{
    public const string VersionUnsupported = "PERSISTENCE_MIGRATION_HISTORY_VERSION_UNSUPPORTED";
    public const string Duplicate = "PERSISTENCE_MIGRATION_HISTORY_DUPLICATE";
    public const string Unknown = "PERSISTENCE_MIGRATION_HISTORY_UNKNOWN";
    public const string NonPrefix = "PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX";
    public const string FrontierMismatch = "PERSISTENCE_MIGRATION_FRONTIER_MISMATCH";
    public const string FactMissing = "PERSISTENCE_MIGRATION_HISTORY_FACT_MISSING";
    public const string OwnerMismatch = "PERSISTENCE_MIGRATION_OWNER_MISMATCH";
    public const string BundleMismatch = "PERSISTENCE_MIGRATION_BUNDLE_MISMATCH";
    public const string ContentMismatch = "PERSISTENCE_MIGRATION_CONTENT_MISMATCH";
    public const string ModelMismatch = "PERSISTENCE_MIGRATION_MODEL_MISMATCH";
    public const string PlanMismatch = "PERSISTENCE_MIGRATION_PLAN_MISMATCH";
    public const string ProviderTargetMismatch = "PERSISTENCE_PROVIDER_TARGET_MISMATCH";
    public const string ProviderTransitionMismatch = "PERSISTENCE_PROVIDER_TRANSITION_MISMATCH";
}

/// <summary>
/// 统一迁移历史与候选计划不一致时抛出的稳定异常。
/// </summary>
public sealed class PersistenceHistoryException : InvalidOperationException
{
    public PersistenceHistoryException(string failureCode, string? migrationId = null)
        : base(
            $"Unified migration history reconciliation failed ({failureCode})"
            + (migrationId is null ? "." : $" at migration '{migrationId}'."))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        FailureCode = failureCode;
        MigrationId = migrationId;
    }

    public string FailureCode { get; }

    public string? MigrationId { get; }
}

/// <summary>
/// 已应用事实与尚待应用后缀的 reconciliation 结果。
/// </summary>
public sealed record UnifiedMigrationHistoryReconciliation(
    IReadOnlyList<CompiledMigrationHistoryEntry> Applied,
    IReadOnlyList<CompiledMigrationHistoryEntry> Pending);

/// <summary>
/// 将数据库执行事实与全局有序的已编译历史对齐。该过程保持纯函数，确保 provider
/// 在发出 DDL 前已经决定所有 history 不一致。
/// </summary>
public static class UnifiedMigrationHistoryReconciler
{
    public static UnifiedMigrationHistoryReconciliation Reconcile(
        string historyVersion,
        IReadOnlyList<CompiledMigrationHistoryEntry> applied,
        IReadOnlyList<CompiledMigrationHistoryEntry> target)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(target);

        if (!string.Equals(
                historyVersion,
                PersistenceHistoryVersions.ReconciliationV1,
                StringComparison.Ordinal))
        {
            throw new PersistenceHistoryException(PersistenceHistoryFailureCodes.VersionUnsupported);
        }

        ValidateTarget(target);
        var targetById = target.ToDictionary(static entry => entry.MigrationId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < applied.Count; index++)
        {
            var actual = applied[index];
            if (!seen.Add(actual.MigrationId))
            {
                throw Failure(PersistenceHistoryFailureCodes.Duplicate, actual);
            }

            if (!targetById.TryGetValue(actual.MigrationId, out var expected))
            {
                throw new PersistenceHistoryException(
                    index >= target.Count
                        ? PersistenceHistoryFailureCodes.FrontierMismatch
                        : PersistenceHistoryFailureCodes.Unknown,
                    actual.MigrationId);
            }

            if (index >= target.Count
                || !string.Equals(actual.MigrationId, target[index].MigrationId, StringComparison.Ordinal)
                || actual.Position != index)
            {
                throw Failure(PersistenceHistoryFailureCodes.NonPrefix, actual);
            }

            ValidateFact(actual, expected);
        }

        return new UnifiedMigrationHistoryReconciliation(
            applied.ToArray(),
            target.Skip(applied.Count).ToArray());
    }

    private static void ValidateTarget(IReadOnlyList<CompiledMigrationHistoryEntry> target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < target.Count; index++)
        {
            var entry = target[index];
            if (entry.Position != index)
            {
                throw Failure(PersistenceHistoryFailureCodes.FrontierMismatch, entry);
            }

            if (!seen.Add(entry.MigrationId))
            {
                throw Failure(PersistenceHistoryFailureCodes.Duplicate, entry);
            }

            ValidateVersions(entry);
        }
    }

    private static void ValidateFact(
        CompiledMigrationHistoryEntry actual,
        CompiledMigrationHistoryEntry expected)
    {
        ValidateVersions(actual);
        if (!string.Equals(actual.OwnerModuleId, expected.OwnerModuleId, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.OwnerMismatch, actual);
        }

        if (!string.Equals(actual.BundleDigest, expected.BundleDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.BundleMismatch, actual);
        }

        if (!string.Equals(actual.ContentDigest, expected.ContentDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ContentMismatch, actual);
        }

        if (!string.Equals(actual.FromModelDigest, expected.FromModelDigest, StringComparison.Ordinal)
            || !string.Equals(actual.ToModelDigest, expected.ToModelDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ModelMismatch, actual);
        }

        if (!string.Equals(actual.PlanDigest, expected.PlanDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.PlanMismatch, actual);
        }

        if (!string.Equals(actual.ProviderTargetVersion, expected.ProviderTargetVersion, StringComparison.Ordinal)
            || !string.Equals(actual.ProviderTargetDigest, expected.ProviderTargetDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ProviderTargetMismatch, actual);
        }

        if (!string.Equals(actual.ProviderTransitionVersion, expected.ProviderTransitionVersion, StringComparison.Ordinal)
            || !string.Equals(actual.ProviderTransitionDigest, expected.ProviderTransitionDigest, StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ProviderTransitionMismatch, actual);
        }
    }

    private static void ValidateVersions(CompiledMigrationHistoryEntry entry)
    {
        if (!string.Equals(
                entry.HistoryVersion,
                PersistenceHistoryVersions.ReconciliationV1,
                StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.VersionUnsupported, entry);
        }

        if (!string.Equals(
                entry.ProviderTargetVersion,
                ProviderTargetSchemaVersions.TargetV1,
                StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ProviderTargetMismatch, entry);
        }

        if (!string.Equals(
                entry.ProviderTransitionVersion,
                ProviderTargetSchemaVersions.TransitionV1,
                StringComparison.Ordinal))
        {
            throw Failure(PersistenceHistoryFailureCodes.ProviderTransitionMismatch, entry);
        }
    }

    private static PersistenceHistoryException Failure(
        string failureCode,
        CompiledMigrationHistoryEntry entry) =>
        new(failureCode, entry.MigrationId);
}

/// <summary>
/// 将每个迁移事实绑定到它之前的不可变全局前缀；追加 migration 不会改变数据库中
/// 已经持久化的前缀摘要。
/// </summary>
public static class UnifiedMigrationHistoryDigest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 计算可随 stream 追加而保持不变的单步 bundle 事实摘要。整份 assembly checksum
    /// 仍由计划编译器独立校验，不能拿会随追加变化的 checksum 充当历史步骤身份。
    /// </summary>
    public static string ComputeBundleDigest(
        string moduleId,
        PersistenceProvider provider,
        CompiledMigrationStep step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(step);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "migration-bundle-fact/v1",
            moduleId,
            provider,
            step.Sequence,
            step.MigrationId,
            step.PreviousContentDigest,
            step.ContentDigest,
            step.FromModelDigest,
            step.ToModelDigest,
            step.Compatibility,
            step.LegacyCutoverDigest
        }, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// 用 #43 Provider Target v1 的 canonical 规则计算 owner-scoped history fact。
    /// 投影保留 owner 表的完整结构与其 FK principal 物理引用，只排除其他 owner 的表。
    /// </summary>
    public static string ComputeProviderTargetFactDigest(ProviderTransitionSnapshot transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return ProviderTargetDigest.Compute(ScopeToOwner(transition.To, transition.ModuleId));
    }

    /// <summary>
    /// 用 #43 Provider Transition v1 的 canonical 规则计算 owner-scoped history fact。
    /// 当前候选的连接前 operation 校验仍消费完整 transition，不使用该历史投影。
    /// </summary>
    public static string ComputeProviderTransitionFactDigest(ProviderTransitionSnapshot transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return ProviderTargetDigest.Compute(transition with
        {
            From = ScopeToOwner(transition.From, transition.ModuleId),
            To = ScopeToOwner(transition.To, transition.ModuleId)
        });
    }

    private static ProviderTargetSnapshot ScopeToOwner(
        ProviderTargetSnapshot target,
        string ownerModuleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModuleId);
        return target with
        {
            Tables = target.Tables
                .Where(table => string.Equals(
                    table.OwnerModuleId,
                    ownerModuleId,
                    StringComparison.Ordinal))
                .ToArray()
        };
    }

    /// <summary>
    /// 为有序事实绑定只依赖当前前缀的计划摘要。
    /// </summary>
    public static IReadOnlyList<CompiledMigrationHistoryEntry> BindPlanDigests(
        IEnumerable<CompiledMigrationHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = new List<CompiledMigrationHistoryEntry>();
        var previous = string.Empty;
        foreach (var entry in entries.OrderBy(static item => item.Position))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                previous,
                entry.Position,
                entry.HistoryVersion,
                entry.MigrationId,
                entry.OwnerModuleId,
                entry.BundleDigest,
                entry.ContentDigest,
                entry.FromModelDigest,
                entry.ToModelDigest,
                entry.ProviderTargetVersion,
                entry.ProviderTargetDigest,
                entry.ProviderTransitionVersion,
                entry.ProviderTransitionDigest
            }, JsonOptions);
            previous = Convert.ToHexStringLower(SHA256.HashData(bytes));
            result.Add(entry with { PlanDigest = previous });
        }

        return result;
    }
}
