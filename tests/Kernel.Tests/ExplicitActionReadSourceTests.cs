using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ExplicitActionReadSourceTests
{
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
