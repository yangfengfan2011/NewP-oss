using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class RetiredButtonDeclarationTests
{
    [Fact]
    public void ReadContract_RejectsTheRetiredButtonAdditionalActionWhitelist()
    {
        const string source = """
            {"dataExecutions":[{"key":"test.item.read","moduleId":"Test","provider":"test.read","boundary":"entry",
              "featureBehaviors":["test.item.read"],"requirements":[{"role":"target","stage":"read","shape":"object",
              "resources":["test.item"],"anyOf":[["read"]]}]EXTRA}]}
            """;
        using var old = JsonDocument.Parse(source.Replace("EXTRA", ",\"allowedAdditionalActions\":[\"edit\"]", StringComparison.Ordinal));
        Assert.Throws<JsonException>(() => DataExecutionContracts.Read(old.RootElement, "Test"));
        using var current = JsonDocument.Parse(source.Replace("EXTRA", "", StringComparison.Ordinal));
        Assert.Equal("read", Assert.Single(Assert.Single(DataExecutionContracts.Read(current.RootElement, "Test")).Requirements).Stage);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public void RetiredStandardWriteContract_CannotRestoreTheSharedActionFloor(string operation)
    {
        using var capsule = JsonDocument.Parse("""
            {"dataExecutions":[{"key":"test.item.OPERATION","moduleId":"Test","provider":"standardCrud.OPERATION",
              "boundary":"entry","featureBehaviors":["test.item.OPERATION"],"resourceKey":"test.item"}]}
            """.Replace("OPERATION", operation, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Read(capsule.RootElement, "Test"));
        var contract = new DataExecutionContract("test.item." + operation, "Test", "standardCrud." + operation,
            "entry", ["test.item." + operation], [new("target", "submit", "object", ["test.item"], [["read"]])]);
        Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Compile([contract], [new("Test", "read", "Read", "data.read")]));
    }

    [Theory]
    [InlineData("Part", "part.part.list", "part.part.permissions")]
    [InlineData("Document", "document.document.list", "document.document.permissions")]
    [InlineData("Workspace", "workspace.folder.list", "workspace.content.permissions")]
    public void ActualNavigationOnlyAuthorization_UsesItsTargetPageReadAtTheOriginalRowPosition(string module, string viewKey, string sourceKey)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "modules");
        var result = new CanonicalModulePlanCompiler().Compile("navigation-read-retirement", new ModuleCapsuleCandidateReader().ReadBuiltIn(root));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var documents = Directory.GetFiles(Path.Combine(root, module, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == viewKey);
        var action = list["view"]!["rowActions"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == "viewAuthorization")!;
        Assert.Equal("view", action["standardAction"]!.GetValue<string>());
        Assert.NotNull(action["navigationTargetKey"]);
        Assert.Equal(sourceKey, action["inputReadSourceKey"]!.GetValue<string>());
        Assert.Null(action["handlerKey"]); Assert.Null(action["dataAccess"]);
        var source = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.ModuleId == module && item.ViewKey == sourceKey && item.Surface == "read");
        var listRead = Assert.Single(result.Plan.ActionDataAccessCatalog, item => item.ModuleId == module && item.ViewKey == viewKey && item.Surface == "read");
        Assert.NotEmpty(listRead.FeaturePositions.Intersect(source.FeaturePositions, StringComparer.Ordinal));
        Assert.DoesNotContain(result.Plan.ActionDataAccessCatalog, item => item.ModuleId == module && item.ViewKey == viewKey && item.ActionKey == "viewAuthorization");
        Assert.Equal(new[] { "read" }, source.Actions);
        Assert.Null(source.BusinessBinding);
    }

    [Theory]
    [InlineData("pageActions")]
    [InlineData("rowActions")]
    [InlineData("featureActions")]
    [InlineData("outputActions")]
    [InlineData("detailContributions")]
    [InlineData("export")]
    public void CompiledButton_CannotBypassAuthoringRetirementWithAnOldDeclaration(string surface)
    {
        var declaration = new ActionDataAccessDeclaration("Test", "items", surface, "legacy", "test.item.read", ["test.item"], []);
        var feature = new FeatureDeclaration("Test", "items", "navigation", "Items", null, "test.item.read")
        { DataResources = ["test.item"], DataAccess = [declaration] };
        DataActionDeclaration[] actions = [new("Test", "read", "Read", "data.read")];
        DataExecutionContract[] sources = [new("test.item.read", "Test", "standardCrud.read", "entry", ["test.item.read"],
            [new("target", "read", "object", ["test.item"], [["read"]])])];
        Assert.Throws<InvalidOperationException>(() => ActionDataAccessCompiler.Compile([feature], actions, sources));
        var read = ActionDataAccessCompiler.Compile([feature with { DataAccess = [declaration with { Surface = "read", ActionKey = "" }] }], actions, sources);
        Assert.Equal("standardCrud.read", Assert.Single(read).Provider);
    }
    [Theory]
    [InlineData("{\"execution\":\"test.item.update\",\"resources\":[\"test.item\"]}")]
    [InlineData("{\"execution\":\"test.item.update\",\"resources\":[\"test.item\"],\"additionalActions\":[\"edit\"]}")]
    [InlineData("{\"object\":\"Item\",\"actions\":[\"edit\"],\"execution\":\"test.item.update\"}")]
    public void ButtonAuthoring_RejectsRetiredOrMixedRequirements(string declaration)
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"entry","featureAuthorization":true,"targetKey":"items"}]}}
            """);
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","resourceKey":"test.item","rowActions":[
              {"key":"edit","standardAction":"edit","dataAccess":DECLARATION}]}}
            """.Replace("DECLARATION", declaration, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, view.RootElement]));
    }

    [Theory]
    [InlineData("part.part.permissions", true)]
    [InlineData("part.part.list", false)]
    [InlineData("unknown.source", false)]
    public void NavigationOnlyInputRead_MustBelongToItsActualTargetPageRef(string sourceKey, bool valid)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "modules", "Part", "metadata");
        using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "..", "plugin.json")));
        var customFeatures = FeatureCatalogCompiler.Read(capsule.RootElement, "Part");
        var documents = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "part.part.list");
        var action = list["view"]!["rowActions"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == "viewAuthorization")!;
        action["inputReadSourceKey"] = sourceKey;
        var input = documents.Select(document => JsonSerializer.SerializeToElement(document)).ToArray();
        if (!valid) { Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Part", input, customFeatures)); return; }
        var features = StandardFeatureMetadataCompiler.Read("Part", input, customFeatures);
        Assert.DoesNotContain(features.SelectMany(feature => feature.DataAccess), declaration => declaration.ActionKey == "viewAuthorization");
        Assert.Contains(features.SelectMany(feature => feature.DataAccess), declaration => declaration.ViewKey == sourceKey && declaration.Surface == "read");
    }
}
