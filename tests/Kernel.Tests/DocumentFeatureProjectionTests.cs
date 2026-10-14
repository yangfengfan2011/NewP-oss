using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class DocumentFeatureProjectionTests
{
    [Fact]
    public void StandardDocument_ExposesOnlyReadCreateDownloadAndRegisteredObjectAuthorization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Document"), "newp.plugin.json"))) root = root.Parent;
        var documents = Directory.GetFiles(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Document"), "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Document", documents));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation");
        Assert.Equal("Document/navigation:document.documents", entry.Position);
        Assert.Equal("document.document.read", entry.BehaviorKey);
        var create = Assert.Single(catalog, point => point.BehaviorKey == "document.document.create");
        Assert.Equal("Document/navigation:document.documents/surface:list.pageActions/action:create", create.Position);
        Assert.Equal(new[] { entry.Position }, create.EntryDependencies);
        var download = Assert.Single(catalog, point => point.BehaviorKey == "document.document.download");
        Assert.Equal("Document/navigation:document.documents/surface:detail.custom/action:download", download.Position);
        Assert.Equal(new[] { entry.Position }, download.EntryDependencies);
        Assert.Equal(new[] { "document.document.authorize", "document.document.create", "document.document.download", "document.document.read" }, catalog.Select(point => point.BehaviorKey).Order(StringComparer.Ordinal));
        FeatureCatalogCompiler.ValidateSelection(catalog, [entry.Position, create.Position, download.Position]);
    }
}
