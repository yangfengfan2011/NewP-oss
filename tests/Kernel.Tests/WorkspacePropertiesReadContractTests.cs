using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspacePropertiesReadContractTests
{
    [Fact]
    public void ActualPropertiesSources_KeepOriginalPositionsAndOneFixedReadForAllThreeResources()
    {
        var result = new CanonicalModulePlanCompiler().Compile("workspace-properties-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var root = Assert.Single(plan.FeatureCatalog, point => point.BehaviorKey == "workspace.spaces.access");
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.Execution == "workspace.properties.read").ToArray();
        Assert.Equal(new[] { "workspace.content.properties", "workspace.content.receivedProperties" }, declarations.Select(item => item.ViewKey).Order().ToArray());
        foreach (var declaration in declarations)
        {
            Assert.Contains(root.Position, declaration.FeaturePositions);
            Assert.Equal("read", declaration.Surface);
            Assert.Equal("workspace.backend", declaration.Provider);
            var role = Assert.Single(declaration.Requirements!);
            Assert.Equal("object", role.Shape); Assert.Equal("read", role.Stage);
            Assert.Equal(new[] { "document.document", "part.part", "workspace.folder" }, role.Resources.Order().ToArray());
            Assert.Equal(new[] { "read" }, Assert.Single(role.AnyOf));
        }
        Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), contract => contract.Key == "workspace.properties.detail");
    }
}
