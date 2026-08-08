namespace ICP.Platform.Persistence.Ownership;

/// <summary>
/// 纯策略层：在任何 schema DDL 前判定目录是否可由当前仓库工作树接管。
/// </summary>
public static class DatabaseOwnershipPolicy
{
    private const string PascalCaseMigration = "20260416171103_AdoptPascalCaseNaming";

    private static readonly IReadOnlySet<string> LegacyCoreTables = new HashSet<string>(
        ["config_items", "auth_roles", "auth_permissions", "auth_role_permissions", "auth_user_roles"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> CurrentCoreTables = new HashSet<string>(
        ["ConfigItem", "Role", "Permission", "RolePermission", "UserRole"],
        StringComparer.OrdinalIgnoreCase);

    public static DatabaseOwnershipDecision Evaluate(
        DatabaseOwnershipMarker expectedMarker,
        DatabaseOwnershipInspection inspection,
        IReadOnlyList<string> knownMigrations)
    {
        ArgumentNullException.ThrowIfNull(expectedMarker);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(knownMigrations);

        if (inspection.Marker is null)
        {
            return inspection.UserTableCount == 0
                ? new DatabaseOwnershipDecision(DatabaseOwnershipDecisionKind.EstablishMarker)
                : Reject(DatabaseOwnershipFailureCodes.DatabaseUnowned);
        }

        if (inspection.Marker != expectedMarker)
        {
            return Reject(DatabaseOwnershipFailureCodes.DatabaseIdentityMismatch);
        }

        if (inspection.AppliedMigrations.Count == 0)
        {
            return inspection.UserTableCount == 0
                ? new DatabaseOwnershipDecision(DatabaseOwnershipDecisionKind.Allow)
                : Reject(DatabaseOwnershipFailureCodes.MigrationHistoryMissing);
        }

        var historyDecision = ValidateMigrationHistory(inspection.AppliedMigrations, knownMigrations);
        if (historyDecision.Kind is DatabaseOwnershipDecisionKind.Reject)
        {
            return historyDecision;
        }

        var requiredTables = inspection.AppliedMigrations.Contains(PascalCaseMigration, StringComparer.Ordinal)
            ? CurrentCoreTables
            : LegacyCoreTables;
        if (!requiredTables.IsSubsetOf(inspection.ExistingTables))
        {
            return Reject(DatabaseOwnershipFailureCodes.LegacyBaselineInvalid);
        }

        return historyDecision;
    }

    private static DatabaseOwnershipDecision ValidateMigrationHistory(
        IReadOnlyList<string> appliedMigrations,
        IReadOnlyList<string> knownMigrations)
    {
        if (knownMigrations.Count == 0
            || !string.Equals(appliedMigrations[0], knownMigrations[0], StringComparison.Ordinal))
        {
            return Reject(DatabaseOwnershipFailureCodes.MigrationHistoryInvalid);
        }

        var knownIndex = 0;
        var databaseAhead = false;
        foreach (var appliedMigration in appliedMigrations)
        {
            if (!databaseAhead
                && knownIndex < knownMigrations.Count
                && string.Equals(appliedMigration, knownMigrations[knownIndex], StringComparison.Ordinal))
            {
                knownIndex++;
                continue;
            }

            if (knownMigrations.Contains(appliedMigration, StringComparer.Ordinal))
            {
                return Reject(DatabaseOwnershipFailureCodes.MigrationHistoryInvalid);
            }

            databaseAhead = true;
        }

        return new DatabaseOwnershipDecision(
            DatabaseOwnershipDecisionKind.Allow,
            DatabaseAhead: databaseAhead);
    }

    private static DatabaseOwnershipDecision Reject(string failureCode) =>
        new(DatabaseOwnershipDecisionKind.Reject, failureCode);
}
