using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
namespace ICP.Platform.Kernel.Tests;
public sealed class FeatureCatalogCompilerTests
{
    [Fact]
    public void Compile_PreservesPlacementAndRequiresItsOwnEntry()
    {
        var catalog = FeatureCatalogCompiler.Compile([
            new("Authorization", "workbench", "navigation", "功能授权", null, "authorization.workbench.read"),
            new("Authorization", "save", "action", "保存", "workbench", "authorization.workbench.save")]);
        Assert.Equal("Authorization/navigation:workbench/action:save", catalog[1].Position);
        Assert.Equal(["Authorization/navigation:workbench"], catalog[1].EntryDependencies);
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [catalog[1].Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, catalog.Select(p => p.Position).ToArray());
    }
    [Fact]
    public void CanonicalPlan_ContainsFeaturesInItsHashAndRejectsBrokenCandidate()
    {
        var candidate = new ModuleCapsuleCandidate("Authorization", "1.0.0", "Authorization", null,
            "packages/Authorization", null, null, null, null, "legacy-module", [], [], [], [], [], [], [],
            new("fixture", "capsule", "/")) { Features = [new("Authorization", "workbench", "navigation", "功能授权", null, "read")] };
        var compiler = new CanonicalModulePlanCompiler();
        var first = compiler.Compile("feature-test", [candidate]);
        Assert.True(first.IsSuccess);
        Assert.Single(first.Plan!.Modules[0].Features);
        var changed = compiler.Compile("feature-test", [candidate with { Features = [candidate.Features[0] with { Title = "新名称" }] }]);
        Assert.NotEqual(first.Plan.PlanHash, changed.Plan!.PlanHash);
        var broken = compiler.Compile("feature-test", [candidate with { Features = [candidate.Features[0] with { ParentKey = "missing" }] }]);
        Assert.False(broken.IsSuccess);
        Assert.Null(broken.Plan);
    }
    [Theory]
    [InlineData("page", null)]
    [InlineData("action", null)]
    [InlineData("navigation", "missing")]
    public void Compile_RejectsIncompleteOrNonAuthorizationNodes(string kind, string? parent) =>
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([new("A", "a", kind, "A", parent, "read")]));
    [Fact]
    public void Compile_RejectsDuplicateAndMissingBindings()
    {
        FeatureDeclaration node = new("A", "a", "navigation", "A", null, "read");
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([node, node]));
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([node with { BehaviorKey = "" }]));
    }
    [Fact]
    public void Selection_RejectsNullPositionAsValidationFailure() =>
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection([], [null!]));
}
