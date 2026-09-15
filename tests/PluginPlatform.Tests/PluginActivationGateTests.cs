using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class PluginActivationGateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "newp-plugin-activation-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompleteAsync_UsesTheApprovedOrderAndCommitsOneActiveSnapshot()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var inventory = CreateInventory();
        var plan = CreateModulePlan("snapshot-1-1", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "legacy-active");
        var gate = CreateGate(events, manager, plan, persistencePlan, projection);

        using var session = gate.Prepare(inventory);

        Assert.Equal(
            ["preflight", "module-plan", "runtime-metadata-validation", "persistence-plan"],
            events);
        Assert.Equal("1.0.0", Assert.Single(manager.Resolve(inventory).ActiveSnapshot!.Modules).Version);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);

        await session.CompleteAsync(
            new RecordingMigrationExecutor(events),
            new RecordingPermissionSynchronizer(events));

        Assert.Equal(
            [
                "preflight",
                "module-plan",
                "runtime-metadata-validation",
                "persistence-plan",
                "migration",
                "runtime-stage",
                "permission-sync",
                "runtime-begin-commit",
                "runtime-complete"
            ],
            events);
        var state = manager.Resolve(inventory);
        Assert.Equal("snapshot-1-1", state.ActiveSnapshot!.SnapshotId);
        Assert.Equal("1.1.0", Assert.Single(state.ActiveSnapshot.Modules).Version);
        Assert.StartsWith("legacy-", state.LastKnownGoodSnapshot!.SnapshotId, StringComparison.Ordinal);
        Assert.Equal("snapshot-1-1", projection.ActiveSnapshotId);
    }

    [Fact]
    public void Prepare_WhenRuntimeMetadataValidationFails_StopsBeforeOtherCandidateWork()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var inventory = CreateInventory();
        var plan = CreateModulePlan("snapshot-1-1", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "legacy-active", failValidation: true);
        var gate = CreateGate(events, manager, plan, persistencePlan, projection);

        var error = Assert.Throws<PluginActivationException>(() => gate.Prepare(inventory));

        Assert.Equal(PluginActivationFailureCodes.RuntimeMetadataValidationFailed, error.FailureCode);
        Assert.False(error.DatabaseAhead);
        Assert.Equal(["preflight", "module-plan", "runtime-metadata-validation"], events);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
    }

    [Fact]
    public async Task CompleteAsync_WhenPermissionSyncFailsAfterMigration_PreservesActiveAndRetryContinues()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var inventory = CreateInventory();
        var plan = CreateModulePlan("snapshot-1-1", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "legacy-active");
        var gate = CreateGate(events, manager, plan, persistencePlan, projection);
        var migration = new RecordingMigrationExecutor(events);

        using (var failedSession = gate.Prepare(inventory))
        {
            var error = await Assert.ThrowsAsync<PluginActivationException>(() =>
                failedSession.CompleteAsync(
                    migration,
                    new RecordingPermissionSynchronizer(events, fail: true)));

            Assert.Equal(PluginActivationFailureCodes.PermissionSyncFailed, error.FailureCode);
            Assert.True(error.DatabaseAhead);
        }

        Assert.Equal("1.0.0", Assert.Single(manager.Resolve(inventory).ActiveSnapshot!.Modules).Version);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
        Assert.True(migration.SafePrefixApplied);

        using var retrySession = gate.Prepare(inventory);
        await retrySession.CompleteAsync(
            migration,
            new RecordingPermissionSynchronizer(events));

        Assert.Equal(2, migration.Attempts);
        Assert.Equal(1, migration.AppliedPrefixes);
        Assert.Equal("snapshot-1-1", manager.Resolve(inventory).ActiveSnapshot!.SnapshotId);
        Assert.Equal("snapshot-1-1", projection.ActiveSnapshotId);
    }

    [Fact]
    public async Task CompleteAsync_WhenPostMigrationConsistencyCheckFails_DoesNotCommitActiveSnapshot()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var inventory = CreateInventory();
        var plan = CreateModulePlan("snapshot-1-1", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "legacy-active");
        var gate = CreateGate(events, manager, plan, persistencePlan, projection);

        using var session = gate.Prepare(inventory);
        var error = await Assert.ThrowsAsync<PluginActivationException>(() =>
            session.CompleteAsync(
                new RecordingMigrationExecutor(events, failAfterApply: true),
                new RecordingPermissionSynchronizer(events)));

        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, error.FailureCode);
        Assert.True(error.DatabaseAhead);
        Assert.Equal(
            [
                "preflight",
                "module-plan",
                "runtime-metadata-validation",
                "persistence-plan",
                "migration"
            ],
            events);
        Assert.Equal("1.0.0", Assert.Single(manager.Resolve(inventory).ActiveSnapshot!.Modules).Version);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
    }

    [Fact]
    public async Task CompleteAsync_WhenActiveStateCommitFails_RollsBackRuntimeProjection()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var inventory = CreateInventory();
        var plan = CreateModulePlan("snapshot-conflict", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "legacy-active");
        var gate = CreateGate(events, manager, plan, persistencePlan, projection);
        var snapshotsRoot = Path.Combine(_root, "lifecycle", "v2", "snapshots");
        Directory.CreateDirectory(snapshotsRoot);
        File.WriteAllText(
            Path.Combine(snapshotsRoot, "snapshot-conflict.json"),
            "{\"snapshotId\":\"snapshot-conflict\",\"planHash\":\"different\"}");

        using var session = gate.Prepare(inventory);
        var error = await Assert.ThrowsAsync<PluginActivationException>(() =>
            session.CompleteAsync(
                new RecordingMigrationExecutor(events),
                new RecordingPermissionSynchronizer(events)));

        Assert.Equal(PluginActivationFailureCodes.ActiveCommitFailed, error.FailureCode);
        Assert.True(error.DatabaseAhead);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
        Assert.Contains("runtime-rollback", events);
        Assert.Equal("1.0.0", Assert.Single(manager.Resolve(inventory).ActiveSnapshot!.Modules).Version);
    }

    [Fact]
    public async Task CompleteAsync_WhenRuntimeStageFails_DoesNotSynchronizeCatalog()
    {
        var events = new List<string>();
        var manager = CreateManager();
        var plan = CreateModulePlan("snapshot-1-1", "1.1.0");
        var projection = new RecordingProjection(events, "legacy-active", failStage: true);
        using var session = CreateGate(events, manager, plan, CreatePersistencePlan(plan), projection).Prepare(CreateInventory());
        var error = await Assert.ThrowsAsync<PluginActivationException>(() => session.CompleteAsync(
            new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events)));
        Assert.Equal(PluginActivationFailureCodes.RuntimeStagingFailed, error.FailureCode);
        Assert.DoesNotContain("permission-sync", events);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
    }
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginLifecycleManager CreateManager() =>
        new(new PluginLifecycleStateStore(_root));

    private static IReadOnlyList<InstalledPluginInventoryItem> CreateInventory() =>
        [new("Partner.AssetCatalog", ["1.0.0", "1.1.0"], "1.0.0")];

    private static PluginActivationGate CreateGate(
        List<string> events,
        PluginLifecycleManager manager,
        CanonicalModulePlan plan,
        CompiledPersistencePlan persistencePlan,
        IPluginRuntimeProjection projection) =>
        new(
            manager,
            new RecordingPreflight(events),
            new RecordingModulePlanCompiler(events, plan),
            new RecordingPersistencePlanCompiler(events, persistencePlan),
            projection);

    private static CanonicalModulePlan CreateModulePlan(string snapshotId, string version)
    {
        var module = new CanonicalModulePlanModule(
            "Partner.AssetCatalog",
            version,
            "Asset Catalog",
            null,
            "packages/Partner.AssetCatalog/" + version,
            null,
            null,
            null,
            "standard-data.v1",
            "behavior-bindings",
            [],
            ["/assets"],
            ["standard-crud:Asset"],
            [],
            ["Asset"],
            new ModulePlanProvenance("installed-package", "newp.plugin.json", "/"));
        return new CanonicalModulePlan(
            "candidate-1-1",
            "1.0",
            new string('a', 64),
            snapshotId,
            [1, 2, 3],
            [module]);
    }

    private static CompiledPersistencePlan CreatePersistencePlan(CanonicalModulePlan plan) =>
        new(
            plan.CandidateId,
            plan.SnapshotId,
            plan.PlanHash,
            PersistenceProvider.SqlServer,
            new string('b', 64),
            [4, 5, 6],
            [],
            []);

    private sealed class RecordingPreflight(List<string> events) : IPluginPackagePreflight
    {
        public void Validate(
            IReadOnlyList<InstalledPluginInventoryItem> inventory,
            ResolvedPluginLifecycleState state) =>
            events.Add("preflight");
    }

    private sealed class RecordingModulePlanCompiler(
        List<string> events,
        CanonicalModulePlan plan) : IPluginCandidatePlanCompiler
    {
        public CanonicalModulePlan Compile(
            IReadOnlyList<InstalledPluginInventoryItem> inventory,
            ResolvedPluginLifecycleState state)
        {
            events.Add("module-plan");
            return plan;
        }
    }



    private sealed class RecordingPersistencePlanCompiler(
        List<string> events,
        CompiledPersistencePlan plan) : IPluginPersistencePlanCompiler
    {
        public CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan)
        {
            events.Add("persistence-plan");
            return plan;
        }
    }

    private sealed class RecordingMigrationExecutor(
        List<string> events,
        bool failAfterApply = false) : IPluginMigrationExecutor
    {
        public bool DatabaseAhead => SafePrefixApplied;

        public bool SafePrefixApplied { get; private set; }

        public int Attempts { get; private set; }

        public int AppliedPrefixes { get; private set; }

        public Task ExecuteAsync(CompiledPersistencePlan plan, CancellationToken cancellationToken)
        {
            events.Add("migration");
            Attempts++;
            if (!SafePrefixApplied)
            {
                SafePrefixApplied = true;
                AppliedPrefixes++;
            }

            if (failAfterApply)
            {
                return Task.FromException(
                    new PersistencePlanExecutionException(
                        PersistenceConsistencyFailureCodes.SchemaTargetMismatch,
                        "Partner.AssetCatalog"));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPermissionSynchronizer(
        List<string> events,
        bool fail = false) : IPluginPermissionSynchronizer
    {
        public Task SynchronizeAsync(CanonicalModulePlan plan, CancellationToken cancellationToken)
        {
            events.Add("permission-sync");
            return fail
                ? Task.FromException(new InvalidOperationException("permission sync failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingProjection(
        List<string> events,
        string activeSnapshotId,
        bool failValidation = false, bool failStage = false) : IPluginRuntimeProjection
    {
        public string ActiveSnapshotId { get; private set; } = activeSnapshotId;

        public void Validate(CanonicalModulePlan plan)
        {
            events.Add("runtime-metadata-validation");
            if (failValidation)
            {
                throw new InvalidOperationException("runtime metadata invalid");
            }
        }

        public IPluginRuntimeStage Stage(CanonicalModulePlan plan)
        {
            events.Add("runtime-stage");
            if (failStage) throw new InvalidOperationException("runtime stage invalid");
            return new RecordingStage(this, events, plan.SnapshotId);
        }

        private sealed class RecordingStage(
            RecordingProjection owner,
            List<string> events,
            string candidateSnapshotId) : IPluginRuntimeStage
        {
            public IPluginRuntimeCommit BeginCommit()
            {
                events.Add("runtime-begin-commit");
                var previous = owner.ActiveSnapshotId;
                owner.ActiveSnapshotId = candidateSnapshotId;
                return new Commit(owner, events, previous);
            }

            public void Dispose()
            {
            }
        }

        private sealed class Commit(
            RecordingProjection owner,
            List<string> events,
            string previousSnapshotId) : IPluginRuntimeCommit
        {
            private bool _completed;

            public void Complete()
            {
                events.Add("runtime-complete");
                _completed = true;
            }

            public void Dispose()
            {
                if (_completed)
                {
                    return;
                }

                events.Add("runtime-rollback");
                owner.ActiveSnapshotId = previousSnapshotId;
            }
        }
    }
}
