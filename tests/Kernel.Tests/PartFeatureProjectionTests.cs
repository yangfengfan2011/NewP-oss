using ICP.Platform.Testing.Authorization;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartFeatureProjectionTests
{
    [Fact]
    public void StandardPart_UsesWorkspaceEntryForReadCreateAndRegisteredObjectAuthorization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Part"), "newp.plugin.json"))) root = root.Parent;
        var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Part"), "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Part"), "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Part", documents, FeatureCatalogCompiler.Read(capsule, "Part"), true));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation" && point.ModuleId == "Part" && point.Key == "part.parts");
        Assert.Equal("Workspace/navigation:workspace/module:Part/navigation:part.parts", entry.Position);
        Assert.Equal("part.part.read", entry.BehaviorKey);
        var create = Assert.Single(catalog, point => point.BehaviorKey == "part.part.create");
        Assert.Equal("Workspace/navigation:workspace/module:Part/navigation:part.parts/surface:list.pageActions/action:create", create.Position);
        Assert.Equal(entry.EntryDependencies.Append(entry.Position), create.EntryDependencies);
        Assert.DoesNotContain(catalog, point => point.BehaviorKey is "part.part.update" or "part.part.delete");
        FeatureCatalogCompiler.ValidateSelection(catalog, RuntimeFeatureCatalogTestSupport.ExpandGrantSet([entry, create]));
    }
}
