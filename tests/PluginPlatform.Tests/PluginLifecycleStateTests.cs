using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class PluginLifecycleStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "newp-plugin-lifecycle-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WhenV2StateIsMissing_UsesLegacyAnchorWithoutWritingState()
    {
        var manager = CreateManager();
        var inventory = CreateInventory(legacyCurrentEnabledVersion: "1.0.0");

        var state = manager.Resolve(inventory);

        Assert.True(state.UsesLegacyFallback);
        Assert.Equal("1.0.0", Assert.Single(state.DesiredModules).Version);
        Assert.Equal("1.0.0", Assert.Single(state.ActiveSnapshot!.Modules).Version);
        Assert.Equal(state.ActiveSnapshot.SnapshotId, state.LastKnownGoodSnapshot!.SnapshotId);
        Assert.False(File.Exists(Path.Combine(_root, "lifecycle", "v2", "state.json")));
    }

    [Fact]
    public void RequestEnable_ChangesOnlyDesiredAndRequiresRestart()
    {
        var manager = CreateManager();
        var inventory = CreateInventory(legacyCurrentEnabledVersion: "1.0.0");
        var before = manager.Resolve(inventory);

        var result = manager.RequestEnable(inventory, "Partner.AssetCatalog", "1.1.0");
        var after = manager.Resolve(inventory);

        Assert.True(result.IsSuccess);
        Assert.True(result.RestartRequired);
        Assert.Equal("PendingRestart", result.Status);
        Assert.Equal("1.1.0", Assert.Single(after.DesiredModules).Version);
        Assert.Equal(before.ActiveSnapshot, after.ActiveSnapshot);
        Assert.Equal(before.LastKnownGoodSnapshot, after.LastKnownGoodSnapshot);
        Assert.Equal("1.0.0", Assert.Single(inventory).LegacyCurrentEnabledVersion);
    }

    [Fact]
    public void RequestDisable_LeavesActiveAndLastKnownGoodUntouched()
    {
        var manager = CreateManager();
        var inventory = CreateInventory(legacyCurrentEnabledVersion: "1.0.0");
        var before = manager.Resolve(inventory);

        var result = manager.RequestDisable(inventory, "Partner.AssetCatalog");
        var after = manager.Resolve(inventory);

        Assert.True(result.IsSuccess);
        Assert.True(result.RestartRequired);
        Assert.False(Assert.Single(after.DesiredModules).Enabled);
        Assert.Equal(before.ActiveSnapshot, after.ActiveSnapshot);
        Assert.Equal(before.LastKnownGoodSnapshot, after.LastKnownGoodSnapshot);
    }

    [Fact]
    public void CommitActive_PromotesCandidateAndKeepsPreviousActiveAsLastKnownGood()
    {
        var manager = CreateManager();
        var inventory = CreateInventory(legacyCurrentEnabledVersion: "1.0.0");
        var previous = manager.Resolve(inventory).ActiveSnapshot!;
        var candidate = CreateSnapshot("snapshot-1-1", "1.1.0");

        manager.CommitActive(inventory, candidate);
        var state = manager.Resolve(inventory);

        Assert.False(state.UsesLegacyFallback);
        Assert.Equal(candidate, state.ActiveSnapshot);
        Assert.Equal(previous, state.LastKnownGoodSnapshot);
    }

    [Fact]
    public void RequestRollback_PointsDesiredAtLastKnownGoodWithoutChangingActive()
    {
        var manager = CreateManager();
        var inventory = CreateInventory(legacyCurrentEnabledVersion: "1.0.0");
        manager.CommitActive(inventory, CreateSnapshot("snapshot-1-1", "1.1.0"));
        var before = manager.Resolve(inventory);

        var result = manager.RequestRollback(inventory, "Partner.AssetCatalog");
        var after = manager.Resolve(inventory);

        Assert.True(result.IsSuccess);
        Assert.True(result.RestartRequired);
        Assert.Equal("1.0.0", Assert.Single(after.DesiredModules).Version);
        Assert.Equal(before.ActiveSnapshot, after.ActiveSnapshot);
        Assert.Equal(before.LastKnownGoodSnapshot, after.LastKnownGoodSnapshot);
    }

    [Fact]
    public void UnconfirmedRawSnapshot_IsHiddenAcrossRestartAndPreservesConfirmedLastKnownGood()
    {
        var committed = new HashSet<string>(StringComparer.Ordinal) { "confirmed-first" };
        var deployment = new TestActivationDeployment(requiresConfirmation: true);
        deployment.ConfirmedSnapshotIds.UnionWith(committed);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, deployment));
        var inventory = CreateInventory(legacyCurrentEnabledVersion: null);
        var first = CreateSnapshot("confirmed-first", "1.0.0");
        manager.CommitActive(inventory, first);
        var candidate = CreateSnapshot("unconfirmed", "1.1.0");
        manager.CommitActive(inventory, candidate);
        var restarted = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, deployment));
        Assert.Null(restarted.Resolve(inventory).ActiveSnapshot);
        Assert.Equal(first, restarted.Resolve(inventory).LastKnownGoodSnapshot);
        Assert.True(restarted.RequestRollback(inventory, "Partner.AssetCatalog").IsSuccess);
        Assert.Equal("1.0.0", Assert.Single(restarted.Resolve(inventory).DesiredModules).Version);
        var raw = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "lifecycle/v2/state.json")))!;
        Assert.Equal(candidate.SnapshotId, raw["activeSnapshotId"]!.GetValue<string>());
        Assert.Equal(first.SnapshotId, raw["lastKnownGoodSnapshotId"]!.GetValue<string>());
        restarted.CommitActive(inventory, candidate);
        deployment.ConfirmedSnapshotIds.Add(candidate.SnapshotId);
        Assert.Equal(candidate, restarted.Resolve(inventory).ActiveSnapshot);
        Assert.Equal(first, restarted.Resolve(inventory).LastKnownGoodSnapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginLifecycleManager CreateManager() =>
        new(new PluginLifecycleStateStore(_root, new TestActivationDeployment(requiresConfirmation: false)));

    private static IReadOnlyList<InstalledPluginInventoryItem> CreateInventory(
        string? legacyCurrentEnabledVersion) =>
        [
            new InstalledPluginInventoryItem(
                "Partner.AssetCatalog",
                ["1.0.0", "1.1.0"],
                legacyCurrentEnabledVersion)
        ];

    private static PluginActiveSnapshot CreateSnapshot(string snapshotId, string version) =>
        new(
            snapshotId,
            new string('a', 64),
            new string('b', 64),
            [new PluginActiveModule("Partner.AssetCatalog", version, "installed-package")]);
}
