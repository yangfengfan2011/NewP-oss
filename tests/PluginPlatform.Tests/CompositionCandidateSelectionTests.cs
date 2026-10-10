using System.Text.Json;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class CompositionCandidateSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-composition-selection", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedVersion_InvalidCompositionRejectsEvenWhenDisabled(bool enabled)
    {
        var first = WritePackage("InforCenter", "1.0.0", ["Missing"]);
        var second = WritePackage("InforCenter", "1.1.0", ["Part"]);
        var part = WritePackage("Part", "1.0.0", []);
        var inventory = new[] { Inventory("InforCenter", first, second), Inventory("Part", part) };
        var desired = new ResolvedPluginLifecycleState(
            [new("InforCenter", "1.0.0", enabled), new("Part", "1.0.0", true)], null, null, false);
        var failure = Assert.Throws<PluginActivationException>(() => Compiler().Compile(inventory, desired));
        Assert.Equal("MODULE_COMPOSITION_MEMBER_MISSING", failure.FailureCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedVersion_DefinesCompleteGraphAndUnselectedInstallationDoesNotChangeIt(bool enabled)
    {
        var first = WritePackage("InforCenter", "1.0.0", ["Part"]);
        var second = WritePackage("InforCenter", "1.1.0", ["Missing"]);
        var part = WritePackage("Part", "1.0.0", []);
        var desired = new ResolvedPluginLifecycleState(
            [new("InforCenter", "1.0.0", enabled), new("Part", "1.0.0", true)], null, null, false);
        var before = Compiler().Compile([Inventory("InforCenter", first), Inventory("Part", part)], desired);
        var after = Compiler().Compile([Inventory("Part", part), Inventory("InforCenter", first, second)], desired);
        Assert.Equal(before.CanonicalBytes, after.CanonicalBytes);
        Assert.Equal(before.PlanHash, after.PlanHash);
        var selected = Assert.Single(after.SelectedModules, module => module.ModuleId == "InforCenter");
        Assert.Equal("1.0.0", selected.Version);
        Assert.Equal(enabled, selected.Enabled);
        Assert.Equal(enabled, after.Modules.Any(module => module.ModuleId == "InforCenter"));
        Assert.Equal("InforCenter", Assert.Single(after.CompositionGraph, node => node.ModuleId == "Part").ParentModuleId);
    }

    private static StandardDataCandidatePlanCompiler Compiler() => new(new StandardDataPackageInspector(
        new PlatformApiHostCatalog(PlatformApiReleaseIdentity.Version, [PlatformApiReleaseIdentity.StandardDataProfile],
            [new(PlatformApiReleaseIdentity.Version, PlatformApiReleaseIdentity.ReleaseDigest)])));

    private static InstalledPluginInventoryItem Inventory(string id, params string[] roots) =>
        new(id, roots.Select(root => Path.GetFileName(root)!).ToArray(), "1.0.0",
            roots.ToDictionary(root => Path.GetFileName(root), root => root, StringComparer.Ordinal));

    private string WritePackage(string id, string version, string[] members)
    {
        var root = Path.Combine(_root, id, version);
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        Directory.CreateDirectory(Path.Combine(root, "frontend"));
        File.WriteAllText(Path.Combine(root, "plugin.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0", moduleId = id, version, displayName = id, composition = new { members }
        }));
        File.WriteAllText(Path.Combine(root, "backend", "plugin.backend.json"), JsonSerializer.Serialize(new { entryAssembly = id + ".dll", dependencies = Array.Empty<string>() }));
        File.WriteAllText(Path.Combine(root, "frontend", "plugin.frontend.json"), "{\"entryManifest\":\"entry.json\"}");
        File.WriteAllText(Path.Combine(root, "frontend", "entry.json"), "{\"entry\":\"entry.js\"}");
        return root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
