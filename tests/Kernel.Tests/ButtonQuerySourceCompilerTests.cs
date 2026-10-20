using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ButtonQuerySourceCompilerTests
{
    [Theory]
    [InlineData("read", true)]
    [InlineData("edit", false)]
    public void ActualPersonExportButton_KeepsItsOwnQueryReadSource(string action, bool valid)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Organization");
        candidates[owner] = candidates[owner] with { Features = candidates[owner].Features.Select(feature => feature with
        {
            DataAccess = feature.DataAccess.Select(declaration => declaration.Execution != "organization.person.export" ? declaration
                : declaration with { Execution = "", Object = "Person", Actions = [action], BusinessBinding = "organization.person.export" }).ToArray()
        }).ToArray() };
        var result = new CanonicalModulePlanCompiler().Compile("person-query-button", candidates);
        if (!valid) { Assert.False(result.IsSuccess); return; }
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var button = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.BusinessBinding == "organization.person.export");
        Assert.Equal("Person", button.Object);
        Assert.Equal(new[] { "read" }, button.Actions);
        Assert.Equal("standardCrud.export", button.Provider);
        var query = Assert.Single(button.Requirements!);
        Assert.Equal(("query", "read", "query"), (query.Role, query.Stage, query.Shape));
        Assert.Equal(new[] { "read" }, Assert.Single(query.AnyOf));
    }
}
