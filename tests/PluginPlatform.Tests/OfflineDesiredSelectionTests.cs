using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class OfflineDesiredSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-offline-selection-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WholeSelection_UpdatesAtomicallyWithoutPublishingActiveOrChangingInventory()
    {
        var inventory = new[] { new InstalledPluginInventoryItem("Part", ["1.0.0", "1.1.0"], "1.0.0"), new InstalledPluginInventoryItem("Workspace", ["1.0.0"], "1.0.0") };
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root));
        var before = manager.Resolve(inventory);
        var result = manager.RequestDesired(inventory, [new("Part", "1.1.0", false), new("Workspace", "1.0.0", false)]);
        Assert.True(result.RestartRequired);
        var selected = manager.Resolve(inventory);
        Assert.Equal(before.ActiveSnapshot, selected.ActiveSnapshot);
        Assert.Equal(before.LastKnownGoodSnapshot, selected.LastKnownGoodSnapshot);
        Assert.Equal("1.0.0", inventory[0].LegacyCurrentEnabledVersion);
        Assert.Equal("1.1.0", Assert.Single(selected.DesiredModules, module => module.ModuleId == "Part").Version);
        Assert.All(selected.DesiredModules, module => Assert.False(module.Enabled));
        var path = Path.Combine(_root, "lifecycle", "v2", "state.json");
        var bytes = File.ReadAllBytes(path);
        Assert.False(manager.RequestDesired(inventory, [new("Part", "1.0.0", true), new("Workspace", "missing", false)]).IsSuccess);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(manager.RequestDesired(inventory, [new("Part", "1.0.0", true), new("Part", "1.1.0", false)]).IsSuccess);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
