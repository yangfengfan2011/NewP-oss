using System.Text.Json;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Persistence;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PluginOfflineDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-271-offline-" + Guid.NewGuid().ToString("N"));

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
