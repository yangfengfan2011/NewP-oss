using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.ComponentBundles;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class OfflineDesiredSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-offline-selection-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WholeSelection_UpdatesAtomicallyWithoutPublishingActiveOrChangingInventory()
    {
        var inventory = new[] { new InstalledPluginInventoryItem("Part", ["1.0.0", "1.1.0"], "1.0.0"), new InstalledPluginInventoryItem("Workspace", ["1.0.0"], "1.0.0") };
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, new TestActivationDeployment(requiresConfirmation: false)));
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

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.2.0")]
    public void BundleVersionDifferences_RequireEveryDecisionAndPreserveWholeDesiredUntilResolved(string packageVersion)
    {
        var inventory = new[]
        {
            new InstalledPluginInventoryItem("D", ["0.9.0", "1.0.0"], null),
            new InstalledPluginInventoryItem("A", ["1.0.0", "1.1.0", "1.2.0"], null),
            new InstalledPluginInventoryItem("Unrelated", ["1.0.0"], null)
        };
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, new TestActivationDeployment(false)));
        Assert.True(manager.RequestDesired(inventory, [new("D", "0.9.0", false), new("A", "1.1.0", false), new("Unrelated", "1.0.0", true)]).IsSuccess);
        var path = Path.Combine(_root, "lifecycle", "v2", "state.json");
        var original = File.ReadAllBytes(path);
        var recorded = manager.ReadRecordedDesired(inventory);
        var bundle = Bundle(packageVersion);
        Assert.Equal(new[] { new BundleVersionDifference("A", "1.1.0", packageVersion), new BundleVersionDifference("D", "0.9.0", "1.0.0") },
            OfflineDesiredSelectionService.DescribeDifferences(bundle, recorded));
        var conflict = Assert.Throws<InvalidDataException>(() => OfflineDesiredSelectionService.SelectBundle(bundle, recorded, true, []));
        Assert.Contains("PLUGIN_BUNDLE_VERSION_CHOICE_REQUIRED", conflict.Message);
        Assert.Contains(packageVersion, conflict.Message);
        Assert.Contains("D", conflict.Message);
        Assert.Throws<InvalidDataException>(() => OfflineDesiredSelectionService.SelectBundle(bundle, recorded, true, [new("A", "1.1.0")]));
        Assert.Equal(original, File.ReadAllBytes(path));
        var selected = OfflineDesiredSelectionService.SelectBundle(bundle, recorded, true, [new("A", "1.1.0"), new("D", "1.0.0")]);
        Assert.True(manager.RequestDesired(inventory, selected).IsSuccess);
        Assert.Equal(new PluginDesiredModuleState("A", "1.1.0", false), selected.Single(item => item.ModuleId == "A"));
        Assert.Equal(new PluginDesiredModuleState("D", "1.0.0", true), selected.Single(item => item.ModuleId == "D"));
        Assert.Equal(new PluginDesiredModuleState("Unrelated", "1.0.0", true), selected.Single(item => item.ModuleId == "Unrelated"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BundleEnable_PreservesExistingMemberChoiceAndSelectOnlyLeavesNewMembersDisabled(bool memberEnabled)
    {
        var bundle = Bundle("1.0.0") with { Components = Bundle("1.0.0").Components.Append(new("New", "1.0.0", "", "", new Dictionary<string, string>())).ToArray() };
        PluginDesiredModuleState[] recorded = [new("D", "1.0.0", false), new("A", "1.0.0", memberEnabled), new("Outside", "1.0.0", false)];
        var enabled = OfflineDesiredSelectionService.SelectBundle(bundle, recorded, true, []);
        Assert.True(enabled.Single(item => item.ModuleId == "D").Enabled);
        Assert.True(enabled.Single(item => item.ModuleId == "New").Enabled);
        Assert.Equal(memberEnabled, enabled.Single(item => item.ModuleId == "A").Enabled);
        Assert.False(enabled.Single(item => item.ModuleId == "Outside").Enabled);
        var selected = OfflineDesiredSelectionService.SelectBundle(bundle, recorded, false, []);
        Assert.False(selected.Single(item => item.ModuleId == "D").Enabled);
        Assert.False(selected.Single(item => item.ModuleId == "New").Enabled);
        Assert.Equal(memberEnabled, selected.Single(item => item.ModuleId == "A").Enabled);
    }

    [Fact]
    public void NewInventoryDefaults_AreNotRecordedByOtherChoicesOrHostActivation()
    {
        var inventory = new[] { new InstalledPluginInventoryItem("A", ["1.0.0"], "1.0.0"), new InstalledPluginInventoryItem("New", ["1.0.0"], null) };
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, new TestActivationDeployment(false)));
        Assert.Single(manager.ReadRecordedDesired(inventory));
        manager.CommitActive(inventory, new PluginActiveSnapshot("first", new string('a', 64), new string('b', 64), [new("A", "1.0.0", "installed-package")]));
        Assert.Single(manager.ReadRecordedDesired(inventory));
        Assert.True(manager.RequestEnable(inventory, "A", "1.0.0").IsSuccess);
        Assert.Single(manager.ReadRecordedDesired(inventory));
        Assert.False(manager.Resolve(inventory).DesiredModules.Single(item => item.ModuleId == "New").Enabled);
        Assert.True(manager.RequestDisable(inventory, "New").IsSuccess);
        Assert.Equal(new PluginDesiredModuleState("New", "1.0.0", false), manager.ReadRecordedDesired(inventory).Single(item => item.ModuleId == "New"));
    }

    private static ComponentBundleManifest Bundle(string version) => new(1, "D", PlatformRuntimeReleaseIdentity.Current, "", [
        new("D", "1.0.0", "", "", new Dictionary<string, string>()), new("A", version, "", "", new Dictionary<string, string>())]);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
