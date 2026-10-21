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
        var managementFeaturePositions = plan.FeatureCatalog.Where(point => point.ModuleId == owner
            && point.BehaviorKey == resource + ".authorize" && point.ActionScope == "list.rowActions").Select(point => point.Position).ToArray();
        Assert.Equal(new[] { readFeature.Position }.Concat(managementFeaturePositions).Order(StringComparer.Ordinal), view.FeaturePositions.Order(StringComparer.Ordinal));
        var read = Assert.Single(view.Requirements!);
        Assert.Equal(("view", "prepare", "object"), (read.Role, read.Stage, read.Shape));
        Assert.Equal(new[] { "read" }, Assert.Single(read.AnyOf));
        Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => item.ViewKey == resource + ".list" && item.ActionKey == "viewAuthorization");
        var management = plan.ActionDataAccessCatalog.Where(item => item.BusinessBinding == resource + ".authorize").ToArray();
        Assert.Equal(new[] { "featureActions", "rowActions" }, management.Select(item => item.Surface).Order(StringComparer.Ordinal));
        Assert.Equal(plan.FeatureCatalog.Where(point => point.ModuleId == owner && point.BehaviorKey == resource + ".authorize")
                .Select(point => point.Position).Order(StringComparer.Ordinal),
            management.SelectMany(item => item.FeaturePositions).Order(StringComparer.Ordinal));
        foreach (var source in management)
        {
            Assert.Equal(owner, source.Object);
            Assert.Equal(new[] { "authorize" }, source.Actions);
            Assert.Null(source.Provider); Assert.Null(source.Requirements);
        }
        Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), contract => contract.Key == resource + ".authorization.manage");
        ExportOrCompareFixture(plan);
    }

    [Fact]
    public void ActualWorkspacePermissions_KeepDeclaredObjectsAndExplicitAuxiliaryReads()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-authorization-consumer",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var sources = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace"
            && (item.Provider == "authorization.collection.view" || item.BusinessBinding == "workspace.folder.authorize")).ToArray();
        Assert.Equal(4, sources.Length);
        Assert.All(sources, source => Assert.Equal(new[] { "workspace.folder", "part.part", "document.document" }, source.Resources));
        var batch = Assert.Single(sources, source => source.ActionKey == "batchAuthorize");
        Assert.Equal(("workspace.folder.list", "pageActions", "batchAuthorize"), (batch.ViewKey, batch.Surface, batch.ActionKey));
        var objects = batch.GetObjectRequirements();
        Assert.Equal(new[] { "WorkspaceFolder", "Part", "Document" }, objects.Select(item => item.Object));
        Assert.All(objects, item => Assert.Equal(new[] { "authorize" }, item.Actions));
        Assert.Null(batch.Requirements); Assert.Null(batch.Provider);
        var read = Assert.Single(sources, source => source.Surface == "read");
        var inputPositions = sources.Where(source => source.ActionKey is "batchAuthorize" or "authorizeContent").SelectMany(source => source.FeaturePositions);
        Assert.Empty(inputPositions.Except(read.FeaturePositions, StringComparer.Ordinal));
        var nonPresented = Assert.Single(sources, source => source.Surface == "featureActions");
        Assert.Empty(nonPresented.FeaturePositions.Intersect(read.FeaturePositions, StringComparer.Ordinal));
        Assert.Single(sources, source => source.Execution == "workspace.content.authorization.view");
        Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => item.ModuleId == "Workspace" && item.ActionKey == "viewAuthorization");
        Assert.All(read.Requirements!, role => Assert.Equal(new[] { "read" }, Assert.Single(role.AnyOf)));
        Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), contract =>
            contract.Key is "workspace.content.authorization.manage" or "workspace.content.authorization.batch");
        ExportOrCompareFixture(plan);
    }

    private static void ExportOrCompareFixture(CanonicalModulePlan plan)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "tests/Kernel.Tests/Fixtures/ObjectAuthorizationDataAccess.contract.json");
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId is "Part" or "Document" or "Workspace"
            && (item.Provider?.StartsWith("authorization.object", StringComparison.Ordinal) == true
                || item.Provider?.StartsWith("authorization.collection", StringComparison.Ordinal) == true
                || item.BusinessBinding is "part.part.authorize" or "document.document.authorize" or "workspace.folder.authorize")).ToArray();
        var json = JsonSerializer.Serialize(declarations, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_OBJECT_AUTHORIZATION_FIXTURE_EXPORT") == "1")
            File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        else Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }
}
