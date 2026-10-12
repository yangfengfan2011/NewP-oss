using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PartFeatureProjectionTests
{
    [Fact]
    public void StandardPart_ExposesOnlyReadCreateAndRegisteredObjectAuthorization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Part"), "newp.plugin.json"))) root = root.Parent;
        var documents = Directory.GetFiles(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Part"), "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Part", documents));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation");
        Assert.Equal("Part/navigation:part.parts", entry.Position);
        Assert.Equal("part.part.read", entry.BehaviorKey);
        var create = Assert.Single(catalog, point => point.BehaviorKey == "part.part.create");
        Assert.Equal("Part/navigation:part.parts/surface:list.pageActions/action:create", create.Position);
        Assert.Equal(new[] { entry.Position }, create.EntryDependencies);
        Assert.Equal(new[] { "part.part.authorize", "part.part.create", "part.part.read" }, catalog.Select(point => point.BehaviorKey).Order(StringComparer.Ordinal));
        FeatureCatalogCompiler.ValidateSelection(catalog, [entry.Position, create.Position]);
    }
}
