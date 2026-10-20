using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ObjectAuthorizationExecutionContractTests
{
    [Theory]
    [InlineData("Part", "part.part", "part.parts")]
    [InlineData("Document", "document.document", "document.documents")]
    public void ActualPermissions_KeepReadOnlyAndManagementSourcesAtExistingFeatures(string owner, string resource, string navigation)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("object-authorization-consumer",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var view = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == resource + ".authorization.view" && item.Surface == "read");
        Assert.Equal((resource + ".permissions", "read", ""), (view.ViewKey, view.Surface, view.ActionKey));
        var readFeature = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == owner && point.Kind == "navigation" && point.Key == navigation);
        Assert.Equal(new[] { readFeature.Position }, view.FeaturePositions);
        var read = Assert.Single(view.Requirements!);
        Assert.Equal(("view", "prepare", "object"), (read.Role, read.Stage, read.Shape));
        Assert.Equal(new[] { "read" }, Assert.Single(read.AnyOf));
        var viewAction = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == view.Execution && item.Surface == "rowActions");
        Assert.Equal((resource + ".list", "viewAuthorization"), (viewAction.ViewKey, viewAction.ActionKey));
        Assert.Equal(view.FeaturePositions, viewAction.FeaturePositions);
        var management = plan.ActionDataAccessCatalog.Where(item => item.Execution == resource + ".authorization.manage").ToArray();
        Assert.Equal(new[] { "featureActions", "rowActions" }, management.Select(item => item.Surface).Order(StringComparer.Ordinal));
        Assert.Equal(plan.FeatureCatalog.Where(point => point.ModuleId == owner && point.BehaviorKey == resource + ".authorize")
                .Select(point => point.Position).Order(StringComparer.Ordinal),
            management.SelectMany(item => item.FeaturePositions).Order(StringComparer.Ordinal));
        foreach (var source in management)
        {
            Assert.All(source.Requirements!.Where(item => item.Role == "target"),
                requirement => Assert.Equal(new[] { "read", "authorize" }, Assert.Single(requirement.AnyOf)));
            Assert.Equal(new[] { "read" }, Assert.Single(Assert.Single(source.Requirements!, item => item.Role == "view").AnyOf));
        }
        ExportOrCompareFixture(plan);
    }

    [Fact]
    public void ActualWorkspacePermissions_KeepTheFiniteCollectionAndEmptyPrepareFloor()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-authorization-consumer",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var sources = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace"
            && item.Provider!.StartsWith("authorization.collection", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, sources.Length);
        Assert.All(sources, source => Assert.Equal(new[] { "workspace.folder", "part.part", "document.document" }, source.Resources));
        var batch = Assert.Single(sources, source => source.Execution == "workspace.content.authorization.batch");
        Assert.Equal(("workspace.folder.list", "pageActions", "batchAuthorize"), (batch.ViewKey, batch.Surface, batch.ActionKey));
        Assert.All(batch.Requirements!.Where(item => item.Role == "target"), role => {
            Assert.Equal("objects", role.Shape);
            Assert.Equal(role.Stage == "prepare" ? 0 : 1, role.MinimumCount);
            Assert.Null(role.MaximumCount);
            Assert.Equal(new[] { "read", "authorize" }, Assert.Single(role.AnyOf));
        });
        Assert.Equal(2, sources.Count(source => source.Execution == "workspace.content.authorization.view"));
        ExportOrCompareFixture(plan);
    }

    private static void ExportOrCompareFixture(CanonicalModulePlan plan)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "tests/Kernel.Tests/Fixtures/ObjectAuthorizationDataAccess.contract.json");
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId is "Part" or "Document" or "Workspace"
            && (item.Provider!.StartsWith("authorization.object", StringComparison.Ordinal)
                || item.Provider.StartsWith("authorization.collection", StringComparison.Ordinal))).ToArray();
        var json = JsonSerializer.Serialize(declarations, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_OBJECT_AUTHORIZATION_FIXTURE_EXPORT") == "1")
            File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        else Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }
}
