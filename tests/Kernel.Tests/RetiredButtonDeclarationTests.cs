using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class RetiredButtonDeclarationTests
{
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
