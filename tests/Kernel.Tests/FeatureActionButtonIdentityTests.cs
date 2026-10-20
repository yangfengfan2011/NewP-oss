using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class FeatureActionButtonIdentityTests
{
    [Theory]
    [InlineData("Identity", "identity.account.detail", "IdentityAccount")]
    [InlineData("Authorization", "authorization.role.detail", "Role")]
    [InlineData("Part", "part.part.list", "Part")]
    [InlineData("Document", "document.document.list", "Document")]
    [InlineData("Workspace", "workspace.folder.list", "WorkspaceFolder")]
    public void ActualNonPresentedFeatureAction_UsesItsExistingBusinessBinding(string moduleId, string viewKey, string model)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, item => item.ModuleId == moduleId);
        var original = candidates[index];
        var root = Path.Combine(AppContext.BaseDirectory, "modules", moduleId);
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var view = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == viewKey)["view"]!;
        var action = view["featureActions"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == "authorize")!;
        var behavior = action["behaviorKey"]!.GetValue<string>();
        var oldExecution = action["dataAccess"]!["execution"]!.GetValue<string>();
        Assert.Null(action["handlerKey"]);
        action["dataAccess"] = new JsonObject { ["object"] = model, ["actions"] = new JsonArray("read", "authorize") };
        using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugin.json")));
        var features = StandardFeatureMetadataCompiler.Read(moduleId, documents.Select(document => JsonSerializer.SerializeToElement(document)),
            FeatureCatalogCompiler.Read(capsule.RootElement, moduleId), includeCustomFeatures: true);
        Assert.Equal(FeatureCatalogCompiler.Compile(candidates.SelectMany(candidate => candidate.Features)).Count,
            FeatureCatalogCompiler.Compile(candidates.Where((_, candidateIndex) => candidateIndex != index).SelectMany(candidate => candidate.Features).Concat(features)).Count);
        candidates[index] = original with { Features = features, DataExecutions = original.DataExecutions.Where(contract => contract.Key != oldExecution
            || features.SelectMany(feature => feature.DataAccess).Any(item => item.Execution == contract.Key)).ToArray() };
        var result = ActionDataAccessCompiler.Compile(candidates.SelectMany(candidate => candidate.Features).ToArray(),
            candidates.SelectMany(candidate => candidate.DataActions).ToArray(), candidates.SelectMany(candidate => candidate.DataExecutions).ToArray(),
            candidates.SelectMany(candidate => candidate.DataExecutionProviders).ToArray());
        var button = Assert.Single(result, item => item.ModuleId == moduleId && item.ViewKey == viewKey && item.Surface == "featureActions" && item.ActionKey == "authorize");
        Assert.Equal(behavior, button.BusinessBinding);
        Assert.Equal(model, button.Object);
        Assert.Equal(new[] { "read", "authorize" }, button.Actions);
        Assert.Empty(button.Execution);
        Assert.Null(action["handlerKey"]);
        Assert.All(button.FeaturePositions, position => Assert.EndsWith("/surface:" + (viewKey.EndsWith("detail", StringComparison.Ordinal) ? "detail" : "list") + ".custom/action:authorize", position));
        var changed = features.Select(feature => feature with { DataAccess = feature.DataAccess.Select(item => item.Id == button.Id
            ? item with { BusinessBinding = behavior + ".wrong" } : item).ToArray() }).ToArray();
        candidates[index] = candidates[index] with { Features = changed };
        Assert.Equal("BUTTON_DATA_ACCESS_DECLARATION_INVALID", Assert.Throws<InvalidOperationException>(() => ActionDataAccessCompiler.Compile(
            candidates.SelectMany(candidate => candidate.Features).ToArray(), candidates.SelectMany(candidate => candidate.DataActions).ToArray(),
            candidates.SelectMany(candidate => candidate.DataExecutions).ToArray(), candidates.SelectMany(candidate => candidate.DataExecutionProviders).ToArray())).Message);
    }

    [Theory]
    [InlineData("pageActions", "\"key\":\"authorize\",\"labelKey\":\"Authorize\",\"behaviorKey\":\"test.item.authorize\"")]
    [InlineData("rowActions", "\"key\":\"authorize\",\"labelKey\":\"Authorize\",\"behaviorKey\":\"test.item.authorize\"")]
    [InlineData("featureActions", "\"key\":\"authorize\",\"titleKey\":\"Authorize\"")]
    public void OrdinaryButtonsAndUnboundFeatures_CannotBorrowTheFeatureOnlyBinding(string surface, string fields)
    {
        using var navigation = JsonDocument.Parse("""{"navigation":{"items":[{"key":"items","featureAuthorization":true,"targetKey":"items"}]}}""");
        using var model = JsonDocument.Parse("""{"Part":{"kind":"Entity"}}""");
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","model":"Part","resourceKey":"test.item","SURFACE":[{FIELDS,"dataAccess":{"object":"Part","actions":["read","authorize"]}}]}}
            """.Replace("SURFACE", surface, StringComparison.Ordinal).Replace("FIELDS", fields, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, model.RootElement, view.RootElement]));
    }
}
