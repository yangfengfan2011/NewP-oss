using ICP.Platform.Persistence.Ownership;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class DatabaseOwnershipPolicyTests
{
    private static readonly WorktreeDatabaseIdentity Identity =
        WorktreeDatabaseIdentity.FromGitPaths(
            @"D:\Work\Git\NewP\.git",
            @"D:\Work\Git\NewP\.git\worktrees\primary");

    private static readonly DatabaseOwnershipMarker ExpectedMarker =
        DatabaseOwnershipMarker.Create(
            Identity,
            "SqlServer",
            "server-fingerprint",
            "catalog-fingerprint");

    private static readonly string[] KnownMigrations =
    [
        "20260409154403_InitialCreate",
        "20260416171103_AdoptPascalCaseNaming",
        "20260808054041_AlignOrganizationSchemaDrift"
    ];

    [Fact]
    public void FromGitPaths_UsesCommonDirectoryForRepositoryAndGitDirectoryForWorktree()
    {
        var anotherWorktree = WorktreeDatabaseIdentity.FromGitPaths(
            @"D:\Work\Git\NewP\.git",
            @"D:\Work\Git\NewP\.git\worktrees\feature-x");

        Assert.Equal(Identity.RepositoryId, anotherWorktree.RepositoryId);
        Assert.NotEqual(Identity.WorktreeId, anotherWorktree.WorktreeId);
        Assert.NotEqual(Identity.CatalogName, anotherWorktree.CatalogName);
        Assert.StartsWith("NewP_", Identity.CatalogName, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_EmptyUnownedCatalog_AllowsMarkerEstablishment()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(null, 0, [], Tables()),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.EstablishMarker, decision.Kind);
        Assert.Null(decision.FailureCode);
    }

    [Fact]
    public void Evaluate_NonEmptyUnownedCatalog_RejectsBeforeSchemaWrites()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(null, 1, [], Tables("UnrelatedTable")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseUnowned, decision.FailureCode);
    }

    [Fact]
    public void Evaluate_MismatchedMarker_RejectsBeforeSchemaWrites()
    {
        var otherIdentity = WorktreeDatabaseIdentity.FromGitPaths(
            @"D:\Work\Git\NewP\.git",
            @"D:\Work\Git\NewP\.git\worktrees\other");
        var actualMarker = DatabaseOwnershipMarker.Create(
            otherIdentity,
            "SqlServer",
            "server-fingerprint",
            "catalog-fingerprint");

        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(actualMarker, 0, [], Tables()),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseIdentityMismatch, decision.FailureCode);
    }

    [Fact]
    public void Evaluate_ExactMarkerAndKnownPrefix_AllowsWarmCatalog()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                5,
                KnownMigrations[..2],
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Allow, decision.Kind);
        Assert.False(decision.DatabaseAhead);
    }

    [Fact]
    public void Evaluate_KnownPrefixWithUnknownTail_AllowsDatabaseAheadWithoutDownMigration()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                5,
                [.. KnownMigrations, "20260901000000_FutureMigration"],
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Allow, decision.Kind);
        Assert.True(decision.DatabaseAhead);
    }

    [Fact]
    public void Evaluate_MigrationHistoryGap_Rejects()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                5,
                [KnownMigrations[0], KnownMigrations[2]],
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(DatabaseOwnershipFailureCodes.MigrationHistoryInvalid, decision.FailureCode);
    }

    [Fact]
    public void Evaluate_MigrationHistoryWithoutPlatformCoreBaseline_Rejects()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                1,
                KnownMigrations[..2],
                Tables("ConfigItem")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(DatabaseOwnershipFailureCodes.LegacyBaselineInvalid, decision.FailureCode);
    }

    private static IReadOnlySet<string> Tables(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}
