using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Persistence;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PluginOfflineUninstallTests
{
    [Theory]
    [InlineData("[\"Document\",\"Part\"]")]
    [InlineData("[\"InforCenter\",\"Part\"]")]
    [InlineData("[\"Part\",\"Audit\"]")]
    [InlineData("[\"Part\",\"PART\"]")]
    [InlineData("[\"Part\",\"Missing\"]")]
    [InlineData("[]")]
    [InlineData("[\"Part\",null]")]
    public void ExplicitSet_RejectsReferencesDependenciesRequiredOrInvalidIdsWithoutPartialRemoval(string requested)
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = Path.Combine(input.Root, "installed");
        var source = PrepareSource(input.Root);
        Assert.Equal(0, Run(root, source, "install-native", "--package", input.Output));
        Assert.Equal(0, Run(root, source, "enable-bundle", "--package", input.Output));
        foreach (var relative in new[] { "persistence/retained/asset.json", "catalog/retained/Part.json", "lifecycle/v2/snapshots/retained.json" })
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "retained recovery material");
        }
        var file = Path.Combine(input.Root, "uninstall.json");
        File.WriteAllText(file, requested);
        var before = HashFiles(root);
        var builtIns = HashFiles(source.ModulesRoot);
        Assert.Equal(1, Run(root, source, "uninstall", "--file", file));
        Assert.Equal(before, HashFiles(root));
        Assert.Equal(builtIns, HashFiles(source.ModulesRoot));
    }

    [Fact]
    public void ExplicitLegalSet_RemovesOnlyRequestedCodeAndSelectionWhilePreservingRecoveryAssets()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = Path.Combine(input.Root, "installed");
        var source = PrepareSource(input.Root);
        Assert.Equal(0, Run(root, source, "install-native", "--package", input.Output));
        Assert.Equal(0, Run(root, source, "enable-bundle", "--package", input.Output));
        Assert.Equal(0, Run(root, source, "disable", "--module-id", "Workspace"));
        var recovery = Path.Combine(root, "persistence", "retained", "asset.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        File.WriteAllText(recovery, "retained models and committed migration history");
        var before = File.ReadAllBytes(recovery);
        var unaffected = HashFiles(Path.Combine(source.ModulesRoot, "Document"));
        var file = Path.Combine(input.Root, "uninstall.json");
        File.WriteAllText(file, "[\"InforCenter\",\"Part\"]");
        Assert.Equal(0, Run(root, source, "uninstall", "--file", file));
        var inventory = InstalledPluginInventoryAdapter.Create(new InstalledPluginStateStore(root));
        Assert.Empty(inventory);
        Assert.False(Directory.Exists(Path.Combine(source.ModulesRoot, "InforCenter")));
        Assert.False(Directory.Exists(Path.Combine(source.ModulesRoot, "Part")));
        Assert.Equal(before, File.ReadAllBytes(recovery));
        Assert.Equal(unaffected, HashFiles(Path.Combine(source.ModulesRoot, "Document")));
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(root,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        var desired = manager.ReadRecordedDesired(inventory);
        Assert.DoesNotContain(desired, item => item.ModuleId is "InforCenter" or "Part");
        Assert.False(desired.Single(item => item.ModuleId == "Workspace").Enabled);
        Assert.True(desired.Single(item => item.ModuleId == "Document").Enabled);
    }

    [Fact]
    public void Uninstall_RejectsRetainedCompositionBeforeRemovingAnyCodeOrState()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = Path.Combine(input.Root, "installed");
        var source = PrepareSource(input.Root);
        Assert.Equal(0, Run(root, source, "install-native", "--package", input.Output));
        Assert.Equal(0, Run(root, source, "enable-bundle", "--package", input.Output));
        var before = HashFiles(root);
        var builtIns = HashFiles(source.ModulesRoot);
        Assert.Equal(1, Run(root, source, "uninstall", "--module-id", "Part"));
        Assert.Equal(before, HashFiles(root));
        Assert.Equal(builtIns, HashFiles(source.ModulesRoot));
        Assert.Contains(InstalledPluginInventoryAdapter.Create(new InstalledPluginStateStore(root)), item => item.ModuleId == "Part");
    }

    private static TrustedBuiltInModuleSource PrepareSource(string root)
    {
        var platform = Path.Combine(root, "platform");
        var modules = Path.Combine(platform, "modules");
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "modules"), "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(modules, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "modules"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        File.Copy(Path.Combine(AppContext.BaseDirectory, "module-composition.json"), Path.Combine(platform, "module-composition.json"));
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            File.Copy(file, Path.Combine(platform, Path.GetFileName(file)));
        return TrustedBuiltInModuleSource.CreateForHostDistribution(platform, modules);
    }

    private static int Run(string root, TrustedBuiltInModuleSource source, string action, params string[] options)
    {
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(root,
            HostPluginActivationDeployment.WithoutDeploymentConfirmation("unused-storage-types.json", PersistenceProvider.SqlServer)));
        return PluginOfflineDeployment.Run(new[] { "plugin-deploy", action }.Concat(options).ToArray(), root, source, false,
            new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], [])), manager);
    }

    private static IReadOnlyDictionary<string, string> HashFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(file => Path.GetRelativePath(root, file),
            file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
}
