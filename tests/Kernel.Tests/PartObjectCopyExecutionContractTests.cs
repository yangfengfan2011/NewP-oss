using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartObjectCopyExecutionContractTests
{
    [Fact]
    public void ActualPartCopyButtonOwnsItsSourceActionWithoutDuplicateInternalContracts()
    {
        var result = new CanonicalModulePlanCompiler().Compile("part-object-copy",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, string.Join(";", result.Diagnostics.Select(item => item.Message)));
        var plan = result.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.pasteObjectCopy");
        Assert.Equal(("Workspace", "workspace.folder.list", "pageActions", "pasteObjectCopy"),
            (declaration.ModuleId, declaration.ViewKey, declaration.Surface, declaration.ActionKey));
        Assert.Empty(declaration.Execution);
        Assert.Null(declaration.Requirements);
        var source = Assert.Single(declaration.GetObjectRequirements(), item => item.Object == "Part");
        Assert.Equal("part.part", source.ResourceKey);
        Assert.Equal(new[] { "partObjectCopy" }, source.Actions);
        Assert.All(declaration.FeaturePositions, position => Assert.Contains(plan.FeatureCatalog,
            feature => feature.Position == position && feature.BehaviorKey == declaration.BusinessBinding));
        Assert.DoesNotContain(declaration.GetObjectRequirements(), item => item.Object == "WorkspaceFolder");
        var part = Assert.Single(plan.Modules, module => module.ModuleId == "Part");
        Assert.DoesNotContain(part.DataExecutions, item => item.Key is "part.part.copy.source" or "part.part.copy.finalCandidate" or "part.part.internal.create");
        Assert.DoesNotContain(plan.Modules.SelectMany(module => module.DataExecutions), item => item.Key == "workspace.objectCopy");
        var resultButton = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.resolveObjectCopy");
        Assert.Empty(resultButton.GetObjectRequirements());
        Assert.Empty(resultButton.Execution);
    }
}
