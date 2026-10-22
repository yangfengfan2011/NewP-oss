using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class PluginActivationGateTests : IDisposable
{
    private TestActivationDeployment _deployment = new(requiresConfirmation: false);
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "newp-plugin-activation-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Prepare_WhenRecoveryArchiveFails_StopsBeforeMigrationAndPreservesTheCause()
    {
        var events = new List<string>();
        var failure = new IOException("recovery archive unavailable");
        var manager = CreateManager(new TestActivationDeployment(requiresConfirmation: true) { PreparationFailure = failure });
        var plan = CreateModulePlan("snapshot-archive", "1.1.0");
        var projection = new RecordingProjection(events, "legacy-active");

        var error = Assert.Throws<PluginActivationException>(() =>
            CreateGate(events, manager, plan, CreatePersistencePlan(plan), projection).Prepare(CreateInventory()));

        Assert.Equal(PluginActivationFailureCodes.DeploymentPreparationFailed, error.FailureCode);
        Assert.False(error.DatabaseAhead);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(["preflight", "module-plan", "runtime-metadata-validation", "persistence-plan", "deployment-prepare"], events);
        Assert.Null(_deployment.Pending);
        Assert.Equal("legacy-active", projection.ActiveSnapshotId);
        Assert.Null(manager.Resolve(CreateInventory()).ActiveSnapshot);
    }

    [Fact]
    public async Task CompleteAsync_WhenFinalConfirmationFails_HidesRawActiveKeepsConfirmedLkgAndCanResume()
    {
        var events = new List<string>();
        var failure = new IOException("final confirmation unavailable");
        var deployment = new TestActivationDeployment(requiresConfirmation: true) { ConfirmationFailure = failure };
        deployment.ConfirmedSnapshotIds.Add("confirmed-first");
        var manager = CreateManager(deployment);
        var inventory = CreateInventory();
        var first = new PluginActiveSnapshot("confirmed-first", new string('c', 64), new string('d', 64),
            [new PluginActiveModule("Partner.AssetCatalog", "1.0.0", "installed-package")]);
        manager.CommitActive(inventory, first);
        var plan = CreateModulePlan("snapshot-confirmation", "1.1.0");
        var persistencePlan = CreatePersistencePlan(plan);
        var projection = new RecordingProjection(events, "confirmed-first");
        using var session = CreateGate(events, manager, plan, persistencePlan, projection).Prepare(inventory);
        Assert.Same(persistencePlan, deployment.Pending);
        Assert.Same(plan, session.ModulePlan);
        Assert.Same(persistencePlan, session.PersistencePlan);

        var error = await Assert.ThrowsAsync<PluginActivationException>(() => session.CompleteAsync(
            new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events)));

        Assert.Equal(PluginActivationFailureCodes.DeploymentConfirmationFailed, error.FailureCode);
        Assert.True(error.DatabaseAhead);
        Assert.Same(failure, error.InnerException);
        Assert.Equal("snapshot-confirmation", projection.ActiveSnapshotId);
        Assert.Same(persistencePlan, deployment.Pending);
        var restarted = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, deployment));
        Assert.Null(restarted.Resolve(inventory).ActiveSnapshot);
        Assert.Equal(first, restarted.Resolve(inventory).LastKnownGoodSnapshot);
        var raw = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "lifecycle/v2/state.json")))!;
        Assert.Equal(plan.SnapshotId, raw["activeSnapshotId"]!.GetValue<string>());
        Assert.True(restarted.RequestRollback(inventory, "Partner.AssetCatalog").IsSuccess);

        deployment.ConfirmationFailure = null;
        await session.CompleteAsync(new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events));
        Assert.Equal(plan.SnapshotId, restarted.Resolve(inventory).ActiveSnapshot!.SnapshotId);
        Assert.Equal(first, restarted.Resolve(inventory).LastKnownGoodSnapshot);
        Assert.Null(deployment.Pending);
        var completed = await Assert.ThrowsAsync<PluginActivationException>(() => session.CompleteAsync(
            new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events)));
        Assert.Equal(PluginActivationFailureCodes.SessionAlreadyCompleted, completed.FailureCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeploymentCancellation_PropagatesWithoutPublishingConfirmation(bool duringPreparation)
    {
        var events = new List<string>();
        var cancellation = new OperationCanceledException();
        var deployment = new TestActivationDeployment(requiresConfirmation: true);
        var manager = CreateManager(deployment);
        var plan = CreateModulePlan("snapshot-cancelled", "1.1.0");
        var gate = CreateGate(events, manager, plan, CreatePersistencePlan(plan), new RecordingProjection(events, "legacy-active"));
        if (duringPreparation)
        {
            deployment.PreparationFailure = cancellation;
            Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(() => gate.Prepare(CreateInventory())));
            Assert.Null(deployment.Pending);
        }
        else
        {
            using var session = gate.Prepare(CreateInventory());
            deployment.ConfirmationFailure = cancellation;
            Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() => session.CompleteAsync(
                new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events))));
            Assert.NotNull(deployment.Pending);
        }
        Assert.Null(manager.Resolve(CreateInventory()).ActiveSnapshot);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsOnlyWhenTheCandidateIsConfirmed()
    {
        var events = new List<string>();
        var manager = CreateManager(new TestActivationDeployment(requiresConfirmation: true));
        var plan = CreateModulePlan("snapshot-confirmation", "1.1.0");
        using var session = CreateGate(events, manager, plan, CreatePersistencePlan(plan),
            new RecordingProjection(events, "legacy-active")).Prepare(CreateInventory());

        await session.CompleteAsync(new RecordingMigrationExecutor(events), new RecordingPermissionSynchronizer(events));

        Assert.Equal(plan.SnapshotId, manager.Resolve(CreateInventory()).ActiveSnapshot?.SnapshotId);
    }

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
            ["preflight", "module-plan", "runtime-metadata-validation", "persistence-plan", "deployment-prepare"],
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
                "deployment-prepare",
                "migration",
                "runtime-stage",
                "permission-sync",
                "runtime-begin-commit",
                "runtime-complete",
                "deployment-complete"
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
                "deployment-prepare",
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

    private PluginLifecycleManager CreateManager(TestActivationDeployment? deployment = null)
    {
        _deployment = deployment ?? new TestActivationDeployment(requiresConfirmation: false);
        return new(new PluginLifecycleStateStore(_root, _deployment));
    }

    private static IReadOnlyList<InstalledPluginInventoryItem> CreateInventory() =>
        [new("Partner.AssetCatalog", ["1.0.0", "1.1.0"], "1.0.0")];

    private PluginActivationGate CreateGate(
        List<string> events,
        PluginLifecycleManager manager,
        CanonicalModulePlan plan,
        CompiledPersistencePlan persistencePlan,
        IPluginRuntimeProjection projection)
    {
        _deployment.Events = events;
        _deployment.Plan = persistencePlan;
        return new(
            manager,
            new RecordingPreflight(events),
            new RecordingModulePlanCompiler(events, plan),
            projection);
    }

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
