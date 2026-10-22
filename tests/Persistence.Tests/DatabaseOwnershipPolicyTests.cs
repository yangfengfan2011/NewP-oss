using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
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
        "20000101000000_TestPolicy_000001_Initial",
        "20000101000001_TestPolicy_000002_Expand",
        "20000101000002_TestPolicy_000003_Transition",
        "20000101000003_TestPolicy_000004_Finalize"
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
        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired, decision.FailureCode);
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
    public void Evaluate_KnownPrefixWithUnknownTail_RejectsDatabaseAhead()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                5,
                [.. KnownMigrations, "20990101000000_TestPolicy_000005_Future"],
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, decision.FailureCode);
    }

    [Fact]
    public void Evaluate_KnownMembers_LeavesOwnerPrefixToStrictReconciliation()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                5,
                [KnownMigrations[0], KnownMigrations[2]],
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Allow, decision.Kind);
    }

    [Fact]
    public void Evaluate_DuplicateAppliedMigration_RejectsWithStableCode()
    {
        var applied = KnownMigrations.ToArray();
        applied[1] = applied[0];

        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                6,
                applied,
                Tables("ConfigItem", "Role", "Permission", "RolePermission", "UserRole")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(PersistenceHistoryFailureCodes.Duplicate, decision.FailureCode);
    }

    [Fact]
    public void Reconcile_ValidGlobalPrefix_ReturnsPendingSuffix()
    {
        var target = BuildHistory();

        var result = UnifiedMigrationHistoryReconciler.Reconcile(
            PersistenceHistoryVersions.ReconciliationV1,
            target[..2],
            target);

        Assert.Equal(target[2..], result.Pending);
    }

    [Fact]
    public void BindPlanDigests_AppendingMigration_PreservesExistingPrefix()
    {
        var target = BuildHistory()
            .Select(static entry => entry with { PlanDigest = string.Empty })
            .ToArray();

        var prefix = UnifiedMigrationHistoryDigest.BindPlanDigests(target[..2]);
        var extended = UnifiedMigrationHistoryDigest.BindPlanDigests(target);

        Assert.Equal(
            prefix.Select(static entry => entry.PlanDigest),
            extended.Take(prefix.Count).Select(static entry => entry.PlanDigest));
    }

    [Fact]
    public void BindPlanDigests_LegacyV1Golden_PreservesPublishedAlgorithm()
    {
        // 冻结旧v1 canonical字段顺序的已知向量；期望值不由被测函数生成。
        Assert.Equal(new[]
        {
            "2600f05203e37da13522d66d5c054e3ee1ba0fc2cb11fec75ab242df8c4824e2",
            "428232150804b0d161da602e16a58b5046ae1523bc38773fe4268566c1d04f7b",
            "705a9a40182f56e119e25e29e99271f65cae9d7990323269c8a32a1b13e730b9"
        }, BuildHistory().Select(entry => entry.PlanDigest));
    }

    [Fact]
    public void Reconcile_DeferredOwner_AppendsAfterActualCommittedOrder()
    {
        var target = UnifiedMigrationHistoryDigest.BindPlanDigests(new[]
        {
            Entry(0, "20000101000000_A_000001", "A", '1'),
            Entry(1, "20000101000001_A_000002", "A", '2'),
            Entry(2, "20000101000002_B_000001", "B", '3'),
            Entry(3, "20000101000003_B_000002", "B", '4')
        });
        var applied = UnifiedMigrationHistoryDigest.BindPlanDigests(new[]
        { target[0], target[2] with { Position = 1 }, target[3] with { Position = 2 } });
        var result = UnifiedMigrationHistoryReconciler.Reconcile(PersistenceHistoryVersions.ReconciliationV1, applied, target);
        Assert.Equal(applied, result.Applied);
        var pending = Assert.Single(result.Pending);
        Assert.Equal("20000101000001_A_000002", pending.MigrationId);
        Assert.Equal(3, pending.Position);
        Assert.NotEqual(target[1].PlanDigest, pending.PlanDigest);
        var completed = UnifiedMigrationHistoryReconciler.Reconcile(PersistenceHistoryVersions.ReconciliationV1, [.. applied, pending], target);
        Assert.Empty(completed.Pending);
        Assert.Equal(applied, completed.Applied.Take(applied.Count));
    }

    [Fact]
    public void Reconcile_OwnerSkipsItsFirstStep_Rejects()
    {
        var target = UnifiedMigrationHistoryDigest.BindPlanDigests(new[]
        { Entry(0, "A_000001", "A", '1'), Entry(1, "A_000002", "A", '2') });
        var applied = UnifiedMigrationHistoryDigest.BindPlanDigests([target[1] with { Position = 0 }]);
        var exception = Assert.Throws<PersistenceHistoryException>(() => UnifiedMigrationHistoryReconciler.Reconcile(PersistenceHistoryVersions.ReconciliationV1, applied, target));
        Assert.Equal(PersistenceHistoryFailureCodes.NonPrefix, exception.FailureCode);
    }

    [Fact]
    public void Reconcile_ReorderedCommittedFactsWithoutNewDigests_Rejects()
    {
        var target = BuildHistory();
        var applied = new[] { target[1] with { Position = 0 }, target[0] with { Position = 1 }, target[2] };
        var exception = Assert.Throws<PersistenceHistoryException>(() => UnifiedMigrationHistoryReconciler.Reconcile(PersistenceHistoryVersions.ReconciliationV1, applied, target));
        Assert.Equal(PersistenceHistoryFailureCodes.PlanMismatch, exception.FailureCode);
    }

    [Fact]
    public void ProviderFactDigests_UnrelatedOwnerAppend_PreservesExistingStep()
    {
        var ownedTable = TableTarget("Partner.AssetCatalog", "Asset");
        var unrelatedTable = TableTarget("Partner.LaterModule", "LaterAsset");
        var original = new ProviderTransitionSnapshot(
            ProviderTargetSchemaVersions.TransitionV1,
            "Partner.AssetCatalog",
            "20260809001000_CreateAssetCatalog",
            Target([]),
            Target([ownedTable]));
        var extended = original with
        {
            From = Target([unrelatedTable]),
            To = Target([ownedTable, unrelatedTable])
        };

        Assert.NotEqual(
            ProviderTargetDigest.Compute(original.To),
            ProviderTargetDigest.Compute(extended.To));
        Assert.NotEqual(
            ProviderTargetDigest.Compute(original),
            ProviderTargetDigest.Compute(extended));
        Assert.Equal(
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(extended));
        Assert.Equal(
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(extended));
    }

    [Fact]
    public void ProviderFactDigests_OwnerTableOrForeignKeyPrincipalChange_ChangesExistingStep()
    {
        var foreignKey = new ProviderForeignKeyTarget(
            "FK_Asset_Principal",
            ["PrincipalId"],
            "Partner.Principal",
            "np_principal",
            "Principal",
            ["Id"],
            "Restrict");
        var ownedTable = TableTarget("Partner.AssetCatalog", "Asset") with
        {
            ForeignKeys = [foreignKey]
        };
        var original = new ProviderTransitionSnapshot(
            ProviderTargetSchemaVersions.TransitionV1,
            "Partner.AssetCatalog",
            "20260809001000_CreateAssetCatalog",
            Target([]),
            Target([ownedTable]));
        var renamedOwnerTable = original with
        {
            To = Target([ownedTable with { Name = "RenamedAsset" }])
        };
        var changedPrincipal = original with
        {
            To = Target([ownedTable with
            {
                ForeignKeys = [foreignKey with { PrincipalTable = "RenamedPrincipal" }]
            }])
        };

        Assert.NotEqual(
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(renamedOwnerTable));
        Assert.NotEqual(
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(renamedOwnerTable));
        Assert.NotEqual(
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(changedPrincipal));
        Assert.NotEqual(
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(original),
            UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(changedPrincipal));
    }

    [Theory]
    [InlineData("unified-history/v2", "PERSISTENCE_MIGRATION_HISTORY_VERSION_UNSUPPORTED")]
    [InlineData("duplicate", "PERSISTENCE_MIGRATION_HISTORY_DUPLICATE")]
    [InlineData("unknown", "PERSISTENCE_MIGRATION_HISTORY_UNKNOWN")]
    [InlineData("non-prefix", "PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX")]
    [InlineData("frontier", "PERSISTENCE_MIGRATION_FRONTIER_MISMATCH")]
    [InlineData("owner", "PERSISTENCE_MIGRATION_OWNER_MISMATCH")]
    [InlineData("bundle", "PERSISTENCE_MIGRATION_BUNDLE_MISMATCH")]
    [InlineData("content", "PERSISTENCE_MIGRATION_CONTENT_MISMATCH")]
    [InlineData("model", "PERSISTENCE_MIGRATION_MODEL_MISMATCH")]
    [InlineData("from-to", "PERSISTENCE_MIGRATION_MODEL_MISMATCH")]
    [InlineData("plan", "PERSISTENCE_MIGRATION_PLAN_MISMATCH")]
    [InlineData("provider-target-version", "PERSISTENCE_PROVIDER_TARGET_MISMATCH")]
    [InlineData("provider-target-digest", "PERSISTENCE_PROVIDER_TARGET_MISMATCH")]
    [InlineData("transition-version", "PERSISTENCE_PROVIDER_TRANSITION_MISMATCH")]
    [InlineData("transition-digest", "PERSISTENCE_PROVIDER_TRANSITION_MISMATCH")]
    public void Reconcile_IncompatibleHistory_FailsClosed(string mutation, string expectedFailureCode)
    {
        var target = BuildHistory();
        var applied = target.ToArray();
        var version = PersistenceHistoryVersions.ReconciliationV1;

        switch (mutation)
        {
            case "unified-history/v2": version = mutation; break;
            case "duplicate": applied[1] = applied[0]; break;
            case "unknown": applied[1] = applied[1] with { MigrationId = "20990101000000_Unknown" }; break;
            case "non-prefix": (applied[0], applied[1]) = (applied[1], applied[0]); break;
            case "frontier": applied = [.. applied, applied[^1] with { Position = 4, MigrationId = "20990101000000_Ahead" }]; break;
            case "owner": applied[1] = applied[1] with { OwnerModuleId = "Wrong.Owner" }; break;
            case "bundle": applied[1] = applied[1] with { BundleDigest = new string('a', 64) }; break;
            case "content": applied[1] = applied[1] with { ContentDigest = new string('b', 64) }; break;
            case "model": applied[1] = applied[1] with { ToModelDigest = new string('c', 64) }; break;
            case "from-to": applied[1] = applied[1] with { FromModelDigest = new string('d', 64) }; break;
            case "plan": applied[1] = applied[1] with { PlanDigest = new string('f', 64) }; break;
            case "provider-target-version": applied[1] = applied[1] with { ProviderTargetVersion = "provider-target/v2" }; break;
            case "provider-target-digest": applied[1] = applied[1] with { ProviderTargetDigest = new string('e', 64) }; break;
            case "transition-version": applied[1] = applied[1] with { ProviderTransitionVersion = "provider-transition/v2" }; break;
            case "transition-digest": applied[1] = applied[1] with { ProviderTransitionDigest = new string('e', 64) }; break;
        }

        var exception = Assert.Throws<PersistenceHistoryException>(() =>
            UnifiedMigrationHistoryReconciler.Reconcile(version, applied, target));

        Assert.Equal(expectedFailureCode, exception.FailureCode);
    }

    [Fact]
    public void Evaluate_MigrationHistoryFromPreviousBaseline_RequiresDatabaseRebuild()
    {
        var decision = DatabaseOwnershipPolicy.Evaluate(
            ExpectedMarker,
            new DatabaseOwnershipInspection(
                ExpectedMarker,
                1,
                ["19990101000000_TestLegacy_000001_Initial"],
                Tables("ConfigItem")),
            KnownMigrations);

        Assert.Equal(DatabaseOwnershipDecisionKind.Reject, decision.Kind);
        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired, decision.FailureCode);
    }

    private static IReadOnlySet<string> Tables(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static CompiledMigrationHistoryEntry[] BuildHistory() => UnifiedMigrationHistoryDigest.BindPlanDigests(
    [
        Entry(0, "20000101000000_TestHistory_000001_Initial", "Test.Owner.Alpha", '1'),
        Entry(1, "20000101000001_TestHistory_000002_Expand", "Test.Owner.Beta", '2'),
        Entry(2, "20000101000002_TestHistory_000003_Finalize", "Test.Owner.Gamma", '3')
    ]).ToArray();

    private static CompiledMigrationHistoryEntry Entry(int position, string migrationId, string owner, char digest) =>
        new(
            position,
            PersistenceHistoryVersions.ReconciliationV1,
            migrationId,
            owner,
            new string(digest, 64),
            new string(digest, 64),
            new string(digest, 64),
            new string(digest, 64),
            new string(digest, 64),
            ProviderTargetSchemaVersions.TargetV1,
            new string(digest, 64),
            ProviderTargetSchemaVersions.TransitionV1,
            new string(digest, 64));

    private static ProviderTargetSnapshot Target(IReadOnlyList<ProviderTableTarget> tables) =>
        new(ProviderTargetSchemaVersions.TargetV1, PersistenceProvider.SqlServer, tables);

    private static ProviderTableTarget TableTarget(string owner, string name) =>
        new(owner, $"np_{name.ToLowerInvariant()}", name, [], null, [], [], [], []);
}
