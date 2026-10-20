using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ButtonImplementationBindingTests
{
    [Fact]
    public void ActualInspectionButton_PreservesItsFeatureWhileBindingTheReadImplementation()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var baseline = new CanonicalModulePlanCompiler().Compile("original-implementation", candidates);
        Assert.True(baseline.IsSuccess, JsonSerializer.Serialize(baseline.Diagnostics));
        var index = Array.FindIndex(candidates, item => item.ModuleId == "Example");
        var original = candidates[index];
        var before = Assert.Single(original.Features, feature => feature.Key == "viewAuthorization" && feature.ActionScope == "list.rowActions");
        var root = Path.Combine(AppContext.BaseDirectory, "modules", "Example");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var view = documents.Single(item => item["view"]?["key"]?.GetValue<string>() == "example.record.list")["view"]!;
        var action = view["rowActions"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == "viewAuthorization")!;
        action["handlerKey"] = "viewAuthorization";
        action["behaviorKey"] = before.BehaviorKey;
        action["dataAccess"] = new JsonObject { ["object"] = "ExampleRecord", ["actions"] = new JsonArray("read", "authorize") };
        using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugin.json")));
        var features = StandardFeatureMetadataCompiler.Read("Example", documents.Select(document => JsonSerializer.SerializeToElement(document)),
            FeatureCatalogCompiler.Read(capsule.RootElement, "Example"), includeCustomFeatures: true);
        var after = Assert.Single(features, feature => feature.ReferenceKey == before.ReferenceKey);
        Assert.Equal(before.BehaviorKey, after.BehaviorKey);
        candidates[index] = original with { Features = features,
            DataExecutions = original.DataExecutions.Where(contract => contract.Key != "example.record.authorization.inspect").ToArray() };
        var result = new CanonicalModulePlanCompiler().Compile("read-implementation", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var binding = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Surface == "rowActions" && item.ViewKey == "example.record.list" && item.ActionKey == "viewAuthorization");
        Assert.Equal("example.record.viewAuthorization", binding.BusinessBinding);
        Assert.Equal(new[] { "read", "authorize" }, binding.Actions);
        var point = Assert.Single(result.Plan.FeatureCatalog, item => item.ReferenceKey == before.ReferenceKey && item.ModuleId == "Example");
        Assert.Equal(before.BehaviorKey, point.BehaviorKey);
        Assert.Contains(point.Position, binding.FeaturePositions);
        Assert.Equal(JsonSerializer.Serialize(baseline.Plan!.FeatureCatalog), JsonSerializer.Serialize(result.Plan.FeatureCatalog));
        Assert.Equal(JsonSerializer.Serialize(baseline.Plan.ActionDataAccessCatalog.Where(item => item.Id != binding.Id)),
            JsonSerializer.Serialize(result.Plan.ActionDataAccessCatalog.Where(item => item.Id != binding.Id)));
    }
}
