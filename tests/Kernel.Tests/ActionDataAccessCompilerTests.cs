using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ActionDataAccessCompilerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("{\"object\":\"Position\",\"actions\":[\"edit\"]}", true)]
    [InlineData("{\"object\":\"Unknown\",\"actions\":[\"edit\"]}", false)]
    [InlineData("{\"object\":\"Position\",\"actions\":[\"unknown\"]}", false)]
    [InlineData("{\"object\":\"Position\",\"actions\":[]}", false)]
    [InlineData("{\"object\":\"Position\",\"actions\":[\"edit\"],\"execution\":\"old\"}", false)]
    [InlineData("{\"object\":\"Position\",\"actions\":[\"edit\"],\"resources\":[\"test.position\"]}", false)]
    public void ObjectActionsButton_CompilesItsOwnRequirementsAndRejectsMalformedDeclarations(string declaration, bool valid)
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}
            """);
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "modules", "Organization", "metadata", "models", "Position.model.json")));
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","model":"Position","resourceKey":"test.position","rowActions":[
              {"key":"change","standardAction":"edit","dataAccess":DECLARATION},
              {"key":"command","handlerKey":"command"}
            ]}}
            """.Replace("DECLARATION", declaration, StringComparison.Ordinal));
        IReadOnlyList<ActionDataAccessRequirement> Compile() => ActionDataAccessCompiler.Compile(
            StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, model.RootElement, view.RootElement]),
            [new("Authorization", "edit", "Edit", "data.edit", RequiresRead: true)]);
        if (!valid) { Assert.Throws<InvalidOperationException>(() => Compile()); return; }
        var requirements = Compile();
        var button = Assert.Single(requirements, item => item.ActionKey == "change");
        Assert.Equal("Position", button.Object);
        Assert.Equal(new[] { "edit" }, button.Actions);
        Assert.Equal("test.position.update", button.BusinessBinding);
        Assert.Equal("", button.Execution);
        Assert.Null(button.Requirements);
        var command = Assert.Single(requirements, item => item.ActionKey == "command");
        Assert.Null(command.Object);
        Assert.Empty(command.Actions);
        Assert.Equal("test.position.command", command.BusinessBinding);
        Assert.NotEmpty(command.FeaturePositions);
    }

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
            // 各真实入口具有自己的资源集合；编译与manifest不能把父要求扩成三个业务目标。
            var authored = candidates.SelectMany(candidate => candidate.Features).SelectMany(feature => feature.DataAccess)
                .Where(declaration => declaration.Id == item.Id).ToArray();
            Assert.NotEmpty(authored);
            Assert.All(authored, declaration => Assert.Equal(declaration.Resources, item.Resources));
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
        Assert.All(history, item =>
        {
            Assert.Equal(new[] { "read" }, item.Actions);
            Assert.Equal("workspace.backend", item.Provider);
            var target = Assert.Single(item.Requirements!);
            Assert.Equal(("target", "read", "object"), (target.Role, target.Stage, target.Shape));
            Assert.Equal(new[] { "read" }, Assert.Single(target.AnyOf));
        });
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

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"read\"]")]
    public void AuthoringDeclaration_RejectsRetiredActionsSyntax(string actions)
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}
            """);
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","resourceKey":"test.item","dataAccess":
              {"execution":"test.list","resources":["test.item"],"actions":ACTIONS}}}
            """.Replace("ACTIONS", actions, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, view.RootElement]));
    }

    [Fact]
    public void AuthoringActionDeclaration_BindsToItsExistingActionPosition()
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}
            """);
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","resourceKey":"test.item","rowActions":[
              {"key":"change","handlerKey":"change","dataAccess":{"execution":"test.change","resources":["test.item"]}}
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
              {"key":"history.entry","navigationKey":"entry","dataAccess":{"execution":"test.history.enter","resources":["test.item"]}}
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
        var index = Array.FindIndex(features, feature => feature.DataAccess.Any(item => item.Execution == "workspace.history.enter"));
        var original = features[index].DataAccess.Single(item => item.Execution == "workspace.history.enter");
        var first = original with { Surface = "rowActions", ActionKey = "same" };
        var second = first with { Surface = "pageActions", AdditionalActions = ["edit"] };
        features[index] = features[index] with { DataAccess = [.. features[index].DataAccess.Where(item => item != original), first, second] };
        candidates[owner] = candidates[owner] with { Features = features,
            DataExecutions = candidates[owner].DataExecutions.Select(contract => contract.Key == "workspace.history.enter"
                ? contract with { AllowedAdditionalActions = ["edit"] } : contract).ToArray() };
        var result = new CanonicalModulePlanCompiler().Compile("independent", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        Assert.Equal(new[] { "read" }, Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Id == first.Id).Actions);
        Assert.Equal(new[] { "read", "edit" }, Assert.Single(result.Plan.ActionDataAccessCatalog, item => item.Id == second.Id).Actions);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
}
