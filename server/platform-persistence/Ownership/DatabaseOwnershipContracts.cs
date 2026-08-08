namespace ICP.Platform.Persistence.Ownership;

public static class DatabaseOwnershipFailureCodes
{
    public const string DatabaseUnowned = "PERSISTENCE_DATABASE_UNOWNED";
    public const string DatabaseIdentityMismatch = "PERSISTENCE_DATABASE_IDENTITY_MISMATCH";
    public const string MarkerInvalid = "PERSISTENCE_DATABASE_MARKER_INVALID";
    public const string MigrationHistoryMissing = "PERSISTENCE_MIGRATION_HISTORY_MISSING";
    public const string MigrationHistoryInvalid = "PERSISTENCE_MIGRATION_HISTORY_INVALID";
    public const string LegacyBaselineInvalid = "PERSISTENCE_LEGACY_BASELINE_INVALID";
}

/// <summary>
/// 数据库中不可变所有权标记的稳定内容。服务端与目录指纹由 provider adapter 从实际连接计算。
/// </summary>
public sealed record DatabaseOwnershipMarker(
    int Version,
    string RepositoryId,
    string WorktreeId,
    string Provider,
    string ServerFingerprint,
    string CatalogFingerprint)
{
    public const int CurrentVersion = 1;

    public static DatabaseOwnershipMarker Create(
        WorktreeDatabaseIdentity identity,
        string provider,
        string serverFingerprint,
        string catalogFingerprint)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFingerprint);

        return new DatabaseOwnershipMarker(
            CurrentVersion,
            identity.RepositoryId,
            identity.WorktreeId,
            provider,
            serverFingerprint,
            catalogFingerprint);
    }
}

public sealed record DatabaseOwnershipInspection(
    DatabaseOwnershipMarker? Marker,
    int UserTableCount,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlySet<string> ExistingTables);

public enum DatabaseOwnershipDecisionKind
{
    EstablishMarker,
    Allow,
    Reject
}

public sealed record DatabaseOwnershipDecision(
    DatabaseOwnershipDecisionKind Kind,
    string? FailureCode = null,
    bool DatabaseAhead = false);

public sealed class DatabaseOwnershipException : InvalidOperationException
{
    public DatabaseOwnershipException(string failureCode)
        : base($"Database ownership validation failed ({failureCode}).")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
