using ICP.Platform.Testing.Authorization;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class DocumentFeatureProjectionTests
{
    [Fact]
    public void StandardDocument_UsesWorkspaceEntryForReadCreateDownloadAndRegisteredObjectAuthorization()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Document"), "newp.plugin.json"))) root = root.Parent;
        var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Document"), "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Document"), "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Document", documents, FeatureCatalogCompiler.Read(capsule, "Document"), true));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation" && point.ModuleId == "Document" && point.Key == "document.documents");
        Assert.Equal("Workspace/navigation:workspace/module:Document/navigation:document.documents", entry.Position);
        Assert.Equal("document.document.read", entry.BehaviorKey);
        var create = Assert.Single(catalog, point => point.BehaviorKey == "document.document.create");
        Assert.Equal("Workspace/navigation:workspace/module:Document/navigation:document.documents/surface:list.pageActions/action:create", create.Position);
        Assert.Equal(entry.EntryDependencies.Append(entry.Position), create.EntryDependencies);
        var download = Assert.Single(catalog, point => point.BehaviorKey == "document.document.download");
        Assert.Equal("Workspace/navigation:workspace/module:Document/navigation:document.documents/surface:detail.custom/action:download", download.Position);
        Assert.Equal(entry.EntryDependencies.Append(entry.Position), download.EntryDependencies);
        Assert.DoesNotContain(catalog, point => point.BehaviorKey is "document.document.update" or "document.document.delete");
        FeatureCatalogCompiler.ValidateSelection(catalog, RuntimeFeatureCatalogTestSupport.ExpandGrantSet([entry, create, download]));
    }
}
