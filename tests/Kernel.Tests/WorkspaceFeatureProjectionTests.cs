using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class WorkspaceFeatureProjectionTests
{
    [Fact]
    public void CustomNavigation_ComposesStandardReadAndCreateWithoutReplacingInitializationBehavior()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "modules/Workspace/newp.plugin.json"))) root = root.Parent;
        var moduleRoot = Path.Combine(root!.FullName, "modules/Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(moduleRoot, "newp.plugin.json")));
        var custom = FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace");
        var documents = Directory.GetFiles(Path.Combine(moduleRoot, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Workspace", documents, custom, includeCustomFeatures: true));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation");
        Assert.Equal("workspace.spaces.access", entry.BehaviorKey);
        Assert.Contains("tree:workspace.structure", entry.ReadBehaviors); Assert.Contains("workspace.folder.read", entry.ReadBehaviors);
        var create = Assert.Single(catalog, point => point.Kind == "action");
        Assert.Equal("Workspace/navigation:workspace/node-type:folder/surface:list.pageActions/action:create", create.Position);
        Assert.Equal("workspace.folder.create", create.BehaviorKey); Assert.Equal(new[] { entry.Position }, create.EntryDependencies);
        Assert.Throws<FeatureSelectionException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [create.Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, [entry.Position, create.Position]);
    }
}
