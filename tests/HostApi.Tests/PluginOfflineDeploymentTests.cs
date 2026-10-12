using System.Text.Json;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Persistence;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PluginOfflineDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-271-offline-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallThenEnableBundle_EnablesNewComponentsWithoutTreatingInventoryDefaultsAsChoices()
    {
        using var input = new ComponentBundleTestInput();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var installRoot = Path.Combine(input.Root, "installed");
        var modulesRoot = Path.Combine(input.Root, "platform", "modules");
        Directory.CreateDirectory(modulesRoot);
        var source = TrustedBuiltInModuleSource.CreateForHost(Path.GetDirectoryName(modulesRoot)!, modulesRoot);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(installRoot,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        var inspector = new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], []));
        Assert.Equal(0, PluginOfflineDeployment.Run(["plugin-deploy", "install-native", "--package", input.Output],
            installRoot, source, false, inspector, manager));
        Assert.False(File.Exists(Path.Combine(installRoot, "lifecycle", "v2", "state.json")));
        Assert.Equal(0, PluginOfflineDeployment.Run(["plugin-deploy", "enable-bundle", "--package", input.Output],
            installRoot, source, false, inspector, manager));
        var selected = manager.Resolve(InstalledPluginInventoryAdapter.Create(new InstalledPluginStateStore(installRoot)));
        Assert.Equal(new PluginDesiredModuleState("InforCenter", "1.0.0", true), Assert.Single(selected.DesiredModules));
        Assert.Null(selected.ActiveSnapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledRealBundle_PreservesMemberAndUnrelatedChoicesAcrossExplicitEnable(bool memberEnabled)
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = Path.Combine(input.Root, "installed");
        Assert.Equal(0, RunOffline(root, false, "install-native", "--package", input.Output));
        var store = new InstalledPluginStateStore(root);
        Assert.True(store.TrySave(new InstalledPluginRecord
        {
            ModuleId = "Unrelated", Version = "1.0.0", CurrentEnabledVersion = "1.0.0", Status = InstalledPluginLifecycleState.Installed,
            PackageRoot = Path.Combine(root, "packages", "Unrelated", "1.0.0"), InstalledVersions = ["1.0.0"]
        }, out _));
        Assert.Equal(0, RunOffline(root, false, "select", "--module-id", "Part", "--version", "1.0.0", "--enabled", memberEnabled.ToString()));
        Assert.Equal(0, RunOffline(root, false, "enable-bundle", "--package", input.Output));
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(root,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        var selected = manager.ReadRecordedDesired(InstalledPluginInventoryAdapter.Create(store));
        Assert.True(selected.Single(item => item.ModuleId == "InforCenter").Enabled);
        Assert.Equal(memberEnabled, selected.Single(item => item.ModuleId == "Part").Enabled);
        Assert.Equal(new PluginDesiredModuleState("Unrelated", "1.0.0", true), selected.Single(item => item.ModuleId == "Unrelated"));
    }

    [Fact]
    public void LegacyExplicitDisable_IsPreservedWhenNoV2DesiredHasEverBeenWritten()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = Path.Combine(input.Root, "installed");
        Assert.Equal(0, RunOffline(root, false, "install-native", "--package", input.Output));
        Assert.Equal("Disabled", new UnifiedPluginPackageOperations(new InstalledPluginStateStore(root)).Disable("Part").Status);
        Assert.False(File.Exists(Path.Combine(root, "lifecycle", "v2", "state.json")));
        Assert.Equal(0, RunOffline(root, false, "enable-bundle", "--package", input.Output));
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "lifecycle", "v2", "state.json")));
        var desired = document.RootElement.GetProperty("desiredModules").EnumerateArray().ToArray();
        Assert.False(desired.Single(item => item.GetProperty("moduleId").GetString() == "Part").GetProperty("enabled").GetBoolean());
        Assert.True(desired.Single(item => item.GetProperty("moduleId").GetString() == "InforCenter").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void DisabledExternalSwitch_RejectsOrdinarySelectionWithoutChangingDesired()
    {
        var installRoot = Path.Combine(_root, "plugins");
        var store = new InstalledPluginStateStore(installRoot);
        Assert.True(store.TrySave(new InstalledPluginRecord
        {
            ModuleId = "External.Extension", Version = "1.0.0", Status = InstalledPluginLifecycleState.Installed,
            PackageRoot = Path.Combine(installRoot, "packages", "External.Extension", "1.0.0"), InstalledVersions = ["1.0.0"]
        }, out _));
        var platformRoot = Path.Combine(_root, "platform");
        var modulesRoot = Path.Combine(platformRoot, "modules");
        Directory.CreateDirectory(modulesRoot);
        var source = TrustedBuiltInModuleSource.CreateForHost(platformRoot, modulesRoot);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(installRoot,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        var inspector = new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], []));
        Assert.Equal(1, PluginOfflineDeployment.Run(["plugin-deploy", "enable", "--module-id", "External.Extension", "--version", "1.0.0"],
            installRoot, source, false, inspector, manager));
        Assert.False(File.Exists(Path.Combine(installRoot, "lifecycle", "v2", "state.json")));
    }

    [Fact]
    public void ConflictingLegacyDesired_ReportsIdentityAndPreservesBothDocuments()
    {
        var canonical = WriteDesired(_root, [new("Part", "1.1.0", false)]);
        var legacy = WriteDesired(Path.Combine(_root, ".feature-gates", "external-plugins-disabled"), [new("Part", "1.0.0", true)]);
        var currentBytes = File.ReadAllBytes(canonical);
        var previousBytes = File.ReadAllBytes(legacy);
        var conflict = Assert.Throws<InvalidDataException>(() => PluginOfflineDeployment.RejectConflictingLegacyDesired(_root));
        Assert.Contains("PLUGIN_DESIRED_SOURCE_CONFLICT", conflict.Message);
        Assert.Contains("Part", conflict.Message);
        Assert.Equal(currentBytes, File.ReadAllBytes(canonical));
        Assert.Equal(previousBytes, File.ReadAllBytes(legacy));
    }

    [Fact]
    public void InvalidLegacyDesired_RejectsWithSourceConflictDiagnostic()
    {
        var legacy = WriteDesired(Path.Combine(_root, ".feature-gates", "external-plugins-disabled"), []);
        File.WriteAllText(legacy, "{}");
        var conflict = Assert.Throws<InvalidDataException>(() => PluginOfflineDeployment.RejectConflictingLegacyDesired(_root));
        Assert.Contains("PLUGIN_DESIRED_SOURCE_CONFLICT", conflict.Message);
    }

    [Fact]
    public void MatchingLegacyDesired_DoesNotCreateAnotherStateRoot()
    {
        var canonical = WriteDesired(_root, [new("Part", "1.0.0", false)]);
        WriteDesired(Path.Combine(_root, ".feature-gates", "external-plugins-disabled"), [new("Part", "1.0.0", false)]);
        var bytes = File.ReadAllBytes(canonical);
        PluginOfflineDeployment.RejectConflictingLegacyDesired(_root);
        Assert.Equal(bytes, File.ReadAllBytes(canonical));
        Assert.False(File.Exists(Path.Combine(_root, ".feature-gates", "external-plugins-disabled", "lifecycle", "v2", "state.json")));
    }

    [Fact]
    public void ReconcileLegacyDesired_PreservesUnrelatedChoicesAndRetiresOldSourceWithoutTouchingRecovery()
    {
        var store = new InstalledPluginStateStore(_root);
        foreach (var id in new[] { "Part", "Unrelated" })
            Assert.True(store.TrySave(new InstalledPluginRecord
            {
                ModuleId = id, Version = "1.1.0", Status = InstalledPluginLifecycleState.Installed,
                PackageRoot = Path.Combine(_root, "packages", id, "1.1.0"), InstalledVersions = ["1.0.0", "1.1.0"]
            }, out _));
        var canonical = WriteDesired(_root, [new("Part", "1.1.0", false), new("Unrelated", "1.0.0", true)]);
        var legacy = WriteDesired(Path.Combine(_root, ".feature-gates", "external-plugins-disabled"), [new("Part", "1.0.0", true)]);
        var previousBytes = File.ReadAllBytes(legacy);
        var recovery = Path.Combine(Path.GetDirectoryName(legacy)!, "snapshots", "retained.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        File.WriteAllText(recovery, "legacy recovery snapshot remains");
        var choices = Path.Combine(_root, "decisions.json");
        File.WriteAllText(choices, "[{\"moduleId\":\"Part\",\"version\":\"1.1.0\",\"enabled\":false}]");
        var currentBytes = File.ReadAllBytes(canonical);
        var incomplete = Path.Combine(_root, "incomplete.json");
        File.WriteAllText(incomplete, "[]");
        Assert.Equal(1, RunOffline(_root, true, "reconcile-desired", "--file", incomplete));
        Assert.Equal(currentBytes, File.ReadAllBytes(canonical));
        Assert.Equal(previousBytes, File.ReadAllBytes(legacy));
        Assert.Equal(0, RunOffline(_root, true, "reconcile-desired", "--file", choices));
        Assert.False(File.Exists(legacy));
        Assert.Equal(previousBytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(legacy)!, "state.reconciled-*.json"))));
        Assert.Equal("legacy recovery snapshot remains", File.ReadAllText(recovery));
        using var document = JsonDocument.Parse(File.ReadAllBytes(canonical));
        var selected = document.RootElement.GetProperty("desiredModules").EnumerateArray().ToArray();
        Assert.Equal(2, selected.Length);
        Assert.Equal("1.1.0", selected.Single(item => item.GetProperty("moduleId").GetString() == "Part").GetProperty("version").GetString());
        Assert.True(selected.Single(item => item.GetProperty("moduleId").GetString() == "Unrelated").GetProperty("enabled").GetBoolean());
        Assert.Equal(0, RunOffline(_root, true, "select", "--module-id", "Part", "--version", "1.0.0"));
        PluginOfflineDeployment.RejectConflictingLegacyDesired(_root);
    }

    private static int RunOffline(string root, bool externalEnabled, string action, params string[] options)
    {
        var modulesRoot = Path.Combine(root, "platform", "modules");
        Directory.CreateDirectory(modulesRoot);
        var source = TrustedBuiltInModuleSource.CreateForHost(Path.GetDirectoryName(modulesRoot)!, modulesRoot);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(root,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        return PluginOfflineDeployment.Run(new[] { "plugin-deploy", action }.Concat(options).ToArray(), root, source, externalEnabled,
            new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], [])), manager);
    }

    private static string WriteDesired(string root, PluginDesiredModuleState[] desired)
    {
        var path = Path.Combine(root, "lifecycle", "v2", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = "2.0", desiredModules = desired }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
