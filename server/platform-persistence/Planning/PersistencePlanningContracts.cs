using ICP.Platform.Modeling.Definitions;
using System.Text.Json.Serialization;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 已编译迁移流中的单个不可变 migration。
/// </summary>
public sealed record CompiledMigrationStep(
    string Sequence,
    string MigrationId,
    string? PreviousContentDigest,
    string ContentDigest,
    string FromModelDigest,
    string ToModelDigest,
    string Compatibility);

/// <summary>
/// 已编译计划与统一迁移事实共同使用的 history reconciliation 版本。
/// </summary>
public static class PersistenceHistoryVersions
{
    public const string ReconciliationV1 = "unified-history/v1";
}

/// <summary>
/// 已编译持久化计划期望的单个全局有序迁移事实。
/// </summary>
public sealed record CompiledMigrationHistoryEntry(
    int Position,
    string HistoryVersion,
    string MigrationId,
    string OwnerModuleId,
    string BundleDigest,
    string ContentDigest,
    string FromModelDigest,
    string ToModelDigest,
    string PlanDigest,
    string ProviderTargetVersion,
    string ProviderTargetDigest,
    string ProviderTransitionVersion,
    string ProviderTransitionDigest);

/// <summary>
/// 一个模块在一个 provider 下的线性迁移流。
/// </summary>
public sealed record ModuleMigrationStream(
    string ModuleId,
    PersistenceProvider Provider,
    string AssemblyPath,
    string ManifestPath,
    string BundleChecksum,
    string ModelDigest,
    string ExpectedFrontier,
    bool IsPlatformStream,
    IReadOnlyList<CompiledMigrationStep> Migrations)
{
    /// <summary>
    /// 计划编译时已验证的程序集映像；迁移执行不得再从 AssemblyPath 打开文件。
    /// </summary>
    [JsonIgnore]
    public VerifiedArtifactImage? AssemblyImage { get; init; }
}

/// <summary>
/// 共享运行时模型与迁移执行共同消费的不可变持久化计划。
/// </summary>
public sealed record CompiledPersistencePlan(
    string CandidateId,
    string SnapshotId,
    string ModulePlanHash,
    PersistenceProvider Provider,
    string PlanDigest,
    byte[] CanonicalBytes,
    IReadOnlyList<ModelDefinition> Models,
    IReadOnlyList<ModuleMigrationStream> Streams)
{
    /// <summary>
    /// 统一 history reconciliation 契约版本；未知版本必须阻断。
    /// </summary>
    public string HistoryVersion { get; init; } = PersistenceHistoryVersions.ReconciliationV1;

    /// <summary>
    /// DbContext 注册消费的计划实例。
    /// </summary>
    public CompiledPersistencePlan RuntimeModelPlan => this;

    /// <summary>
    /// migration runner 消费的计划实例。
    /// </summary>
    public CompiledPersistencePlan MigrationPlan => this;
}

/// <summary>
/// 持久化计划编译的稳定失败信号。
/// </summary>
public sealed record PersistencePlanDiagnostic(
    string Code,
    string ModuleId,
    string CanonicalKey,
    string SourcePath,
    string Message);

/// <summary>
/// 持久化计划编译结果；失败时不返回部分计划。
/// </summary>
public sealed record PersistencePlanCompilationResult(
    CompiledPersistencePlan? Plan,
    IReadOnlyList<PersistencePlanDiagnostic> Diagnostics)
{
    public bool IsSuccess => Plan is not null && Diagnostics.Count == 0;
}

/// <summary>
/// 编译计划进入 provider 执行阶段后的稳定失败码。
/// </summary>
public static class PersistenceMigrationFailureCodes
{
    public const string AssemblyInvalid = "PERSISTENCE_MIGRATION_ASSEMBLY_INVALID";
    public const string MigrationMissing = "PERSISTENCE_MIGRATION_MISSING";
    public const string MigrationUndeclared = "PERSISTENCE_MIGRATION_UNDECLARED";
    public const string MigrationIdConflict = "PERSISTENCE_MIGRATION_ID_CONFLICT";
    public const string MigrationLockUnavailable = "PERSISTENCE_MIGRATION_LOCK_UNAVAILABLE";
    public const string NullValuesPresent = "PERSISTENCE_MIGRATION_NULL_VALUES_PRESENT";
    public const string OperationForbidden = "PERSISTENCE_MIGRATION_OPERATION_FORBIDDEN";
    public const string ProviderUnsupported = "PERSISTENCE_MIGRATION_PROVIDER_UNSUPPORTED";
    public const string TableNotEmpty = "PERSISTENCE_MIGRATION_TABLE_NOT_EMPTY";
}

/// <summary>
/// migration 完成后的只读一致性检查失败码。
/// </summary>
public static class PersistenceConsistencyFailureCodes
{
    public const string SchemaTargetMismatch = "PERSISTENCE_SCHEMA_TARGET_MISMATCH";
}

/// <summary>
/// migration bundle 在连接前静态检查失败时抛出的稳定异常。
/// </summary>
public sealed class PersistencePlanExecutionException : InvalidOperationException
{
    public PersistencePlanExecutionException(
        string failureCode,
        string moduleId,
        string? migrationId = null,
        Exception? innerException = null)
        : base(
            $"Persistence plan execution failed ({failureCode}) for module '{moduleId}'"
            + (migrationId is null ? "." : $", migration '{migrationId}'."),
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        FailureCode = failureCode;
        ModuleId = moduleId;
        MigrationId = migrationId;
    }

    public string FailureCode { get; }

    public string ModuleId { get; }

    public string? MigrationId { get; }
}
