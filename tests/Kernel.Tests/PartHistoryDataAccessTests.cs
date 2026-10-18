using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartHistoryDataAccessTests
{
    [Fact]
    public void PartAuthoring_ProjectsEachButtonToTheSameFormalRequirementsWithoutNewFeatures()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiled = new CanonicalModulePlanCompiler().Compile("part-history", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Part").ToArray();
        Assert.NotEmpty(declarations);
        Assert.All(declarations, item =>
        {
            Assert.Equal(new[] { "read" }, item.Actions);
            Assert.Equal(new[] { "part.part" }, item.Resources);
        });
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Part");
        var count = Directory.GetFiles(Path.Combine(root, "metadata", "views"), "*.json").Sum(path =>
        {
            using var part = JsonDocument.Parse(File.ReadAllText(path));
            var view = part.RootElement.GetProperty("view");
            return (view.TryGetProperty("dataAccess", out _) ? 1 : 0) + new[] { "pageActions", "rowActions", "outputActions", "detailContributions" }
                .Sum(surface => view.TryGetProperty(surface, out var actions) ? actions.EnumerateArray().Count(action => action.TryGetProperty("dataAccess", out _)) : 0);
        });
        Assert.Equal(count, declarations.Length);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var fixture = JsonSerializer.Deserialize<ActionDataAccessRequirement[]>(File.ReadAllText(Path.Combine(repository.FullName,
            "tests/Kernel.Tests/Fixtures/PartHistoryDataAccess.contract.json")), options);
        Assert.Equal(JsonSerializer.Serialize(fixture, options), JsonSerializer.Serialize(declarations, options));
        using var manifest = JsonDocument.Parse(new MetadataRuntimeManifestBuilder().Build(AppContext.BaseDirectory,
            plan.Modules.Select(module => module.ModuleId).ToArray(), plan.Modules.Select(module => module.ModuleId).ToArray(), plan.FeatureCatalog, plan.ActionDataAccessCatalog));
        var projected = manifest.RootElement.GetProperty("modules").EnumerateArray().Single(module => module.GetProperty("moduleId").GetString() == "Part").GetProperty("dataAccess");
        Assert.Equal(JsonSerializer.Serialize(declarations, options), JsonSerializer.Serialize(projected.Deserialize<ActionDataAccessRequirement[]>(options), options));
        var history = Assert.Single(plan.FeatureCatalog, feature => feature.ModuleId == "Part" && feature.BehaviorKey == "part.part.history");
        Assert.All(declarations, declaration => Assert.Equal(new[] { history.Position }, declaration.FeaturePositions));
        using var features = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, "tests/Kernel.Tests/Fixtures/PartHistoryFeatures.contract.json")));
        Assert.Equal(features.RootElement.GetProperty("historyPosition").GetString(), history.Position);
        Assert.DoesNotContain(plan.FeatureCatalog, feature => feature.ParentPosition == history.Position);
    }
}
