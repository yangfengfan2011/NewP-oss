using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ExplicitActionReadSourceTests
{
    [Fact]
    public void ActualModuleGraph_CompilesWithUniqueFormalFeatureReferences()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var result = new CanonicalModulePlanCompiler().Compile("explicit-action-read", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        Assert.All(result.Plan!.FeatureCatalog.GroupBy(point => (point.ModuleId, point.ReferenceKey)), group => Assert.Single(group));
    }

    [Fact]
    public void DuplicateAuthoringActions_AreRejectedBeforeReferenceExpansion()
    {
        using var navigation = JsonDocument.Parse("""{"navigation":{"items":[{"key":"items","featureAuthorization":true,"targetKey":"items"}]}}""");
        using var view = JsonDocument.Parse("""{"view":{"key":"items","mode":"list","resourceKey":"test.items","pageActions":[{"key":"command","handlerKey":"command"},{"key":"command","handlerKey":"command"}]}}""");
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, view.RootElement]));
    }

    [Fact]
    public void SameDetailActionReference_ExpandsOnceAcrossListAndRoutePresentation()
    {
        using var navigation = JsonDocument.Parse("""
            {"navigation":{"items":[{"key":"items","featureAuthorization":true,"targetKey":"items"},
              {"key":"detail","featureAuthorization":false,"routeOnly":true,"featureOwner":"navigation:items","targetKey":"detail"}]}}
            """);
        using var model = JsonDocument.Parse("""{"Position":{"kind":"Entity"}}""");
        using var list = JsonDocument.Parse("""{"view":{"key":"items","mode":"list","model":"Position","resourceKey":"test.position","detailViewKey":"detail"}}""");
        using var detail = JsonDocument.Parse("""{"view":{"key":"detail","mode":"detail","model":"Position","resourceKey":"test.position","pageActions":[{"key":"command","handlerKey":"command"}]}}""");
        var features = StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, model.RootElement, list.RootElement, detail.RootElement]);
        var points = FeatureCatalogCompiler.Compile(features);
        Assert.Single(points, point => point.BehaviorKey == "test.position.command");
        Assert.Single(features.SelectMany(feature => feature.DataAccess), declaration => declaration.ActionKey == "command");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ExplicitResourceRead_ProjectsTheExistingListSourceToTheActualAction(bool detail, bool explicitRead)
    {
        using var navigation = JsonDocument.Parse("""{"navigation":{"items":[{"key":"items","featureAuthorization":true,"targetKey":"items"}]}}""");
        using var model = JsonDocument.Parse("""{"Position":{"kind":"Entity"}}""");
        var action = """{"key":"command","handlerKey":"command","readBehaviors":READS}"""
            .Replace("READS", explicitRead ? "[\"test.position.read\"]" : "[]", StringComparison.Ordinal);
        using var list = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","model":"Position","resourceKey":"test.position","detailViewKey":"detail",
              "dataAccess":{"execution":"test.position.list","resources":["test.position"]},"pageActions":ACTIONS}}
            """.Replace("ACTIONS", detail ? "[]" : "[" + action + "]", StringComparison.Ordinal));
        using var details = JsonDocument.Parse("""
            {"view":{"key":"detail","mode":"detail","model":"Position","resourceKey":"test.position",
              "dataAccess":{"execution":"test.position.read","resources":["test.position"]},"pageActions":ACTIONS}}
            """.Replace("ACTIONS", detail ? "[" + action + "]" : "[]", StringComparison.Ordinal));
        var features = StandardFeatureMetadataCompiler.Read("Test", [navigation.RootElement, model.RootElement, list.RootElement, details.RootElement]);
        var command = Assert.Single(features, feature => feature.BehaviorKey == "test.position.command");
        var reads = command.DataAccess.Where(item => item.Surface == "read").ToArray();
        if (!explicitRead) Assert.Empty(reads);
        else
        {
            var read = Assert.Single(reads);
            Assert.Equal("items", read.ViewKey);
            Assert.Equal("test.position.list", read.Execution);
        }
    }
}
