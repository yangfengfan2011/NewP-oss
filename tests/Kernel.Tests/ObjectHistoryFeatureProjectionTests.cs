using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Kernel.Tests;

public sealed class ObjectHistoryFeatureProjectionTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void WorkspaceHistory_UsesOneIndependentHistoryIdentityForTheSafeListAndReadOnlyInputs()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Workspace", documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace"), includeCustomFeatures: true));
        var read = Assert.Single(points, point => point.Kind == "navigation" && point.Key == "workspace");
        var history = Assert.Single(points, point => point.BehaviorKey == "workspace.folder.history");
        Assert.Equal("navigation", history.Kind);
        Assert.Equal(read.Position, Assert.Single(history.EntryDependencies));
        Assert.Contains("workspace.history.read", history.ReadBehaviors);
        Assert.DoesNotContain(points, point => point.ParentPosition == history.Position);
        FeatureCatalogCompiler.ValidateSelection(points, [read.Position, history.Position]);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName, "tests", "Kernel.Tests", "Fixtures", "WorkspaceHistoryFeatures.contract.json")));
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("historyPosition").GetString(), history.Position);
        output.WriteLine(JsonSerializer.Serialize(new { readPosition = read.Position, historyPosition = history.Position }));
    }
    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("Part", "part", "part.parts")]
    [InlineData("Document", "document", "document.documents")]
    public void IndependentHistories_KeepOneRealHistoryPointForListDetailsAndOutputs(string module, string slug, string entryKey)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, module);
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read(module, documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, module), includeCustomFeatures: true));
        var read = Assert.Single(points, point => point.Kind == "navigation" && point.Key == entryKey);
        var history = Assert.Single(points, point => point.BehaviorKey == slug + "." + slug + ".history");
        Assert.Equal(read.Position, Assert.Single(history.EntryDependencies));
        Assert.Contains(slug + ".history.read", history.ReadBehaviors);
        Assert.DoesNotContain(points, point => point.ParentPosition == history.Position);
        FeatureCatalogCompiler.ValidateSelection(points, [read.Position, history.Position]);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName, "tests", "Kernel.Tests", "Fixtures", module + "HistoryFeatures.contract.json")));
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("historyPosition").GetString(), history.Position);
        output.WriteLine(JsonSerializer.Serialize(new { module, readPosition = read.Position, historyPosition = history.Position }));
    }

}
