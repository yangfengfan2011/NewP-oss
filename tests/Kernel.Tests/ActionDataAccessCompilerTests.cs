using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ActionDataAccessCompilerTests(ITestOutputHelper output)
{
    [Fact]
    public void FormalPlan_ProjectsWorkspaceButtonsWithoutChangingFeaturePositions()
    {
        var candidates = Candidates();
        var result = new CanonicalModulePlanCompiler().Compile("declarations", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        foreach (var module in plan.ActionDataAccessCatalog.GroupBy(item => item.ModuleId))
            output.WriteLine("ACTION_DATA_ACCESS:" + module.Key + "=" + JsonSerializer.Serialize(module.ToArray(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var requirements = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace").ToArray();
        Assert.NotEmpty(requirements);
        Assert.All(requirements, item =>
        {
            Assert.Equal(new[] { "workspace.folder", "part.part", "document.document" }, item.Resources);
            Assert.All(item.FeaturePositions, position => Assert.Contains(plan.FeatureCatalog, point => point.Position == position && point.ModuleId == item.ModuleId));
        });
        var manifest = new MetadataRuntimeManifestBuilder().Build(AppContext.BaseDirectory, plan.Modules.Select(module => module.ModuleId).ToArray(),
            plan.Modules.Select(module => module.ModuleId).ToArray(), plan.FeatureCatalog, plan.ActionDataAccessCatalog);
        using var document = JsonDocument.Parse(manifest);
        var projected = document.RootElement.GetProperty("modules").EnumerateArray().Single(module => module.GetProperty("moduleId").GetString() == "Workspace")
            .GetProperty("dataAccess");
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(json, projected.GetRawText().Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        output.WriteLine("WORKSPACE_DATA_ACCESS=" + json);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var fixture = JsonSerializer.Deserialize<ActionDataAccessRequirement[]>(File.ReadAllText(Path.Combine(repository.FullName,
            "tests/Kernel.Tests/Fixtures/WorkspaceHistoryDataAccess.contract.json")), options);
        var historyIds = fixture!.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var history = requirements.Where(item => historyIds.Contains(item.Id)).ToArray();
        Assert.All(history, item => Assert.Equal(new[] { "read" }, item.Actions));
        Assert.Equal(JsonSerializer.Serialize(history, options), JsonSerializer.Serialize(fixture, options));
    }

    [Theory]
    [InlineData("unknown-action")]
    [InlineData("wrong-owner")]
    [InlineData("unknown-resource")]
    [InlineData("virtual-resource")]
    [InlineData("identity-conflict")]
    public void FormalPlan_RejectsInvalidButtonDeclarations(string problem)
    {
        var candidates = Candidates();
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var features = candidates[owner].Features.ToArray();
        var index = Array.FindIndex(features, feature => feature.DataAccess.Count > 0);
        var original = features[index].DataAccess[0];
        var invalid = problem switch {
            "unknown-action" => original with { Actions = ["unregistered"] },
            "wrong-owner" => original with { ModuleId = "Part" },
            "unknown-resource" => original with { Resources = ["unregistered.resource"] },
            "virtual-resource" => original with { Resources = ["workspace.history"] },
            _ => original
        };
        features[index] = features[index] with { DataAccess = problem == "identity-conflict" ? [original, invalid] : [invalid] };
        candidates[owner] = candidates[owner] with { Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile("invalid", candidates).IsSuccess);
    }

    [Fact]
    public void AuthoringActionDeclaration_BindsToItsExistingActionPosition()
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}
            """);
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","resourceKey":"test.item","rowActions":[
              {"key":"change","handlerKey":"change","dataAccess":{"execution":"test.change","resources":["test.item"],"actions":["edit"]}}
            ]}}
            """);
        var features = StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, view.RootElement]);
        var owner = Assert.Single(features, feature => feature.DataAccess.Count > 0);
        Assert.Equal("action", owner.Kind);
        Assert.Equal("change", owner.Key);
        Assert.Equal("test.item.change", owner.BehaviorKey);
    }

    [Fact]
    public void DetailContribution_UsesItsOwnDeclarationAtTheOriginalNavigation()
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"history"}]}}
            """);
        using var view = JsonDocument.Parse("""
            {"view":{"key":"history","mode":"list","resourceKey":"test.history","detailContributions":[
              {"key":"history.entry","navigationKey":"entry","dataAccess":{"execution":"test.history.enter","resources":["test.item"],"actions":["read"]}}
            ]}}
            """);
        var feature = Assert.Single(StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, view.RootElement]));
        Assert.Equal("navigation", feature.Kind);
        Assert.Equal("entry", feature.Key);
        var declaration = Assert.Single(feature.DataAccess);
        Assert.Equal("detailContributions", declaration.Surface);
        Assert.Equal("Test/view:history/detailContributions/action:history.entry", declaration.Id);
    }

    [Fact]
    public void SameExecutionButtons_KeepIndependentDataRequirementsAndStructuralIdentities()
    {
        var candidates = Candidates();
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var features = candidates[owner].Features.ToArray();
        var index = Array.FindIndex(features, feature => feature.DataAccess.Count > 0);
        var first = features[index].DataAccess[0] with { Surface = "rowActions", ActionKey = "same", Actions = ["read"] };
        var second = first with { Surface = "pageActions", Actions = ["edit"] };
        features[index] = features[index] with { DataAccess = [.. features[index].DataAccess.Skip(1), first, second] };
        candidates[owner] = candidates[owner] with { Features = features };
        var result = new CanonicalModulePlanCompiler().Compile("independent", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        Assert.Equal(new[] { "read" }, Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Id == first.Id).Actions);
        Assert.Equal(new[] { "edit" }, Assert.Single(result.Plan.ActionDataAccessCatalog, item => item.Id == second.Id).Actions);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
}
