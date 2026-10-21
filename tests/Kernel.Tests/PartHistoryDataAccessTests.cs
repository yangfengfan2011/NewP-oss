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
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Part" && (item.Execution == "part.history.list" || item.BusinessBinding == "part.part.history")).ToArray();
        Assert.NotEmpty(declarations);
        Assert.All(declarations, item =>
        {
            Assert.Equal(new[] { "read" }, item.Actions);
            Assert.Equal(new[] { "part.part" }, item.Resources);
            if (item.BusinessBinding is not null)
            {
                Assert.Equal("Part", item.Object);
                Assert.Null(item.Requirements);
            }
            else
            {
                Assert.Equal("part.backend", item.Provider);
                var target = Assert.Single(item.Requirements!);
                Assert.Equal(("target", "read", "object"), (target.Role, target.Stage, target.Shape));
                Assert.Equal(new[] { "read" }, Assert.Single(target.AnyOf));
            }
        });
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Part");
        var count = Directory.GetFiles(Path.Combine(root, "metadata", "views"), "*.json").Sum(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var view = document.RootElement.GetProperty("view");
            var ownHistory = view.GetProperty("key").GetString() == "part.history.list";
            return (ownHistory && view.TryGetProperty("dataAccess", out _) ? 1 : 0) + new[] { "pageActions", "rowActions", "outputActions", "detailContributions" }
                .Sum(surface => view.TryGetProperty(surface, out var actions) ? actions.EnumerateArray().Count(action => action.TryGetProperty("dataAccess", out _)
                    && (ownHistory || action.GetProperty("key").GetString() == "history")) : 0);
        });
        Assert.Equal(count, declarations.Length);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var fixture = JsonSerializer.Deserialize<ActionDataAccessRequirement[]>(File.ReadAllText(Path.Combine(repository.FullName,
            "tests/Kernel.Tests/Fixtures/PartHistoryDataAccess.contract.json")), options);
        Assert.Equal(JsonSerializer.Serialize(fixture, options), JsonSerializer.Serialize(declarations, options));
        using var manifest = JsonDocument.Parse(new MetadataRuntimeManifestBuilder().Build(AppContext.BaseDirectory,
            plan.Modules.Select(module => module.ModuleId).ToArray(), plan.Modules.Select(module => module.ModuleId).ToArray(), plan.FeatureCatalog, plan.ActionDataAccessCatalog));
        var projected = manifest.RootElement.GetProperty("modules").EnumerateArray().Single(module => module.GetProperty("moduleId").GetString() == "Part").GetProperty("dataAccess");
        Assert.Equal(JsonSerializer.Serialize(declarations, options), JsonSerializer.Serialize(projected.Deserialize<ActionDataAccessRequirement[]>(options)!
            .Where(item => (item.Execution == "part.history.list" || item.BusinessBinding == "part.part.history")), options));
        var history = Assert.Single(plan.FeatureCatalog, feature => feature.ModuleId == "Part" && feature.BehaviorKey == "part.part.history");
        Assert.All(declarations, declaration => Assert.Equal(new[] { history.Position }, declaration.FeaturePositions));
        using var features = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, "tests/Kernel.Tests/Fixtures/PartHistoryFeatures.contract.json")));
        Assert.Equal(features.RootElement.GetProperty("historyPosition").GetString(), history.Position);
        Assert.DoesNotContain(plan.FeatureCatalog, feature => feature.ParentPosition == history.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartHistory_RejectsUnknownActionsAndRetiredExecution(bool additional)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var owner = Array.FindIndex(candidates, item => item.ModuleId == "Part");
        var features = candidates[owner].Features.ToArray();
        var index = Array.FindIndex(features, feature => feature.DataAccess.Any(item => item.BusinessBinding == "part.part.history"));
        features[index] = features[index] with { DataAccess = features[index].DataAccess.Select(item => item.BusinessBinding != "part.part.history" ? item
            : additional ? item with { Execution = "retired.execution" } : item with { Actions = ["unknown"] }).ToArray() };
        candidates[owner] = candidates[owner] with { Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile("part-history-invalid-input", candidates).IsSuccess);
    }
}
