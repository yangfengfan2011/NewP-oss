using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Ownership;

/// <summary>
/// 纯策略层：在任何 schema DDL 前判定目录是否可由当前仓库工作树接管。
/// </summary>
public static class DatabaseOwnershipPolicy
{
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
                : Reject(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired);
        }

        if (inspection.Marker != expectedMarker)
        {
            return Reject(DatabaseOwnershipFailureCodes.DatabaseIdentityMismatch);
        }

        if (inspection.AppliedMigrations.Count == 0)
        {
            return inspection.UserTableCount == 0
                ? new DatabaseOwnershipDecision(DatabaseOwnershipDecisionKind.Allow)
                : Reject(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired);
        }

        var historyDecision = ValidateMigrationHistory(inspection.AppliedMigrations, knownMigrations);
        if (historyDecision.Kind is DatabaseOwnershipDecisionKind.Reject)
        {
            return historyDecision;
        }

        return historyDecision;
    }

    private static DatabaseOwnershipDecision ValidateMigrationHistory(
        IReadOnlyList<string> appliedMigrations,
        IReadOnlyList<string> knownMigrations)
    {
        if (knownMigrations.Count == 0)
        {
            return Reject(PersistenceHistoryFailureCodes.FrontierMismatch);
        }

        if (appliedMigrations
            .GroupBy(static migrationId => migrationId, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            return Reject(PersistenceHistoryFailureCodes.Duplicate);
        }

        for (var index = 0; index < appliedMigrations.Count; index++)
        {
            var appliedMigration = appliedMigrations[index];
            if (index < knownMigrations.Count
                && string.Equals(appliedMigration, knownMigrations[index], StringComparison.Ordinal))
            {
                continue;
            }

            if (knownMigrations.Contains(appliedMigration, StringComparer.Ordinal))
            {
                return Reject(PersistenceHistoryFailureCodes.NonPrefix);
            }

            if (index == 0)
            {
                return Reject(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired);
            }

            return Reject(index >= knownMigrations.Count
                ? PersistenceHistoryFailureCodes.FrontierMismatch
                : PersistenceHistoryFailureCodes.Unknown);
        }

        return new DatabaseOwnershipDecision(DatabaseOwnershipDecisionKind.Allow);
    }

    private static DatabaseOwnershipDecision Reject(string failureCode) =>
        new(DatabaseOwnershipDecisionKind.Reject, failureCode);
}
