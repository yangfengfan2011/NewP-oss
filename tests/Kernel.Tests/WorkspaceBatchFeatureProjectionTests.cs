using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Kernel.Tests;

public sealed class WorkspaceBatchFeatureProjectionTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void BatchHeader_UsesTheFormalAuthorizeActionAndExistingReadDependency()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Workspace", documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace"), includeCustomFeatures: true));
        var read = Assert.Single(catalog, point => point.Kind == "navigation" && point.Key == "workspace");
        var batch = Assert.Single(catalog, point => point.Key == "batchAuthorize" && point.ActionScope == "list.pageActions");
        var single = Assert.Single(catalog, point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var copy = Assert.Single(catalog, point => point.Key == "copySelected" && point.ActionScope == "list.pageActions");
        Assert.Equal("workspace.folder.authorize", batch.BehaviorKey);
        Assert.Equal(single.BehaviorKey, batch.BehaviorKey);
        Assert.NotEqual(single.Position, batch.Position);
        Assert.Contains("workspace.folder.read", batch.ReadBehaviors);
        FeatureCatalogCompiler.ValidateSelection(catalog, [read.Position, batch.Position]);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName,
            "tests", "Kernel.Tests", "Fixtures", "WorkspaceBatchPermissionFeatures.contract.json")));
        Assert.Equal(read.Position, fixture.RootElement.GetProperty("readPosition").GetString());
        Assert.Equal(batch.Position, fixture.RootElement.GetProperty("batchPosition").GetString());
        Assert.Equal(copy.Position, fixture.RootElement.GetProperty("copyPosition").GetString());
        output.WriteLine("WORKSPACE_BATCH_FEATURES=" + JsonSerializer.Serialize(new { readPosition = read.Position,
            batchPosition = batch.Position, copyPosition = copy.Position }));
    }
}
