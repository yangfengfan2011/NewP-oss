using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ButtonObjectRequirementsCompilerTests
{
    [Fact]
    public void MultipleObjects_CompileIndependentActionsAndMergeTheSameAuthoringAtActualPositions()
    {
        var first = Declaration();
        var second = Declaration();
        var compiled = ActionDataAccessCompiler.Compile(Features(first, second), Actions());
        var button = Assert.Single(compiled);
        Assert.Null(button.Object);
        Assert.Empty(button.Actions);
        Assert.Equal(2, button.FeaturePositions.Count);
        Assert.Equal(new[] { "Company", "Person" }, button.GetObjectRequirements().Select(item => item.Object));
        Assert.Equal(new[] { "read" }, button.GetObjectRequirements()[0].Actions);
        Assert.Equal(new[] { "edit" }, button.GetObjectRequirements()[1].Actions);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate-object")]
    [InlineData("duplicate-resource")]
    [InlineData("unknown-resource")]
    [InlineData("empty-actions")]
    [InlineData("duplicate-actions")]
    [InlineData("unknown-action")]
    [InlineData("mixed")]
    [InlineData("resources-mismatch")]
    public void MultipleObjects_RejectMalformedOrContradictoryRequirements(string problem)
    {
        var valid = Declaration();
        var invalid = problem switch
        {
            "empty" => valid with { Resources = [], ObjectRequirements = [] },
            "duplicate-object" => valid with { ObjectRequirements = [new("Company", ["read"], "test.company"), new("Company", ["edit"], "test.person")] },
            "duplicate-resource" => valid with { ObjectRequirements = [new("Company", ["read"], "test.company"), new("Person", ["edit"], "test.company")] },
            "unknown-resource" => valid with { Resources = ["test.unknown"], ObjectRequirements = [new("Company", ["read"], "test.unknown")] },
            "empty-actions" => valid with { Resources = ["test.company"], ObjectRequirements = [new("Company", [], "test.company")] },
            "duplicate-actions" => valid with { Resources = ["test.company"], ObjectRequirements = [new("Company", ["read", "read"], "test.company")] },
            "unknown-action" => valid with { Resources = ["test.company"], ObjectRequirements = [new("Company", ["unknown"], "test.company")] },
            "mixed" => valid with { Object = "Company", Actions = ["read"] },
            _ => valid with { Resources = ["test.company"] }
        };
        Assert.Throws<InvalidOperationException>(() => ActionDataAccessCompiler.Compile(Features(invalid), Actions()));
    }

    private static ActionDataAccessDeclaration Declaration() => new("Test", "shared", "rowActions", "apply", "", ["test.company", "test.person"], [])
    { BusinessBinding = "test.apply", ObjectRequirements = [new("Company", ["read"], "test.company"), new("Person", ["edit"], "test.person")] };
    private static DataActionDeclaration[] Actions() => [new("Authorization", "read", "Read", "read"), new("Authorization", "edit", "Edit", "edit", RequiresRead: true)];
    private static FeatureDeclaration[] Features(params ActionDataAccessDeclaration[] declarations) => declarations.SelectMany((declaration, index) => new FeatureDeclaration[]
    {
        new("Test", "entry" + index, "navigation", "Entry", null, "test.read") { DataResources = ["test.company", "test.person"] },
        new("Test", "apply", "action", "Apply", "entry" + index, "test.apply") { ReferenceKey = "apply" + index, DataAccess = [declaration] }
    }).ToArray();
}
