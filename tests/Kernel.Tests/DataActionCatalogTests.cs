using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class DataActionCatalogTests
{
    [Fact]
    public void Authoring_DefaultsEnabledAndPreservesDisabledReference()
    {
        using var document = JsonDocument.Parse("""{"dataActions":[{"key":"read","title":"读取","requiresRead":false,"behaviorKey":"data.read"},{"key":"edit","title":"编辑","enabled":false,"requiresRead":true,"behaviorKey":"data.edit"}]}""");
        var declarations = DataActionCatalogCompiler.Read(document.RootElement, "Authorization");
        var catalog = DataActionCatalogCompiler.Compile(declarations);
        Assert.True(catalog.Single(action => action.Key == "read").Enabled);
        Assert.False(catalog.Single(action => action.Key == "edit").Enabled);
        Assert.Throws<InvalidOperationException>(() => DataActionCatalogCompiler.Compile(declarations.Concat(declarations)));
    }

    [Fact]
    public void FormalPlan_UsesAuthoringSwitchAndIncludesItInCandidateIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "data-action-authoring-" + Guid.NewGuid().ToString("N"));
        var module = Path.Combine(root, "Authorization");
        Directory.CreateDirectory(module);
        var capsule = """{"moduleId":"Authorization","version":"1.0.0","displayName":"授权","frontend":{"enabled":false},"dataActions":[{"key":"read","title":"读取","behaviorKey":"data.read","enabled":ENABLED}]}""";
        try
        {
            CanonicalModulePlan Read(bool enabled)
            {
                File.WriteAllText(Path.Combine(module, "plugin.json"), capsule.Replace("ENABLED", enabled ? "true" : "false"));
                var result = new CanonicalModulePlanCompiler().Compile("switch", new ModuleCapsuleCandidateReader().ReadBuiltIn(root));
                Assert.True(result.IsSuccess);
                return result.Plan!;
            }
            var enabled = Read(true);
            var disabled = Read(false);
            Assert.True(Assert.Single(enabled.DataActionCatalog).Enabled);
            Assert.False(Assert.Single(disabled.DataActionCatalog).Enabled);
            Assert.NotEqual(enabled.PlanHash, disabled.PlanHash);
            Assert.Equal(enabled.PlanHash, Read(true).PlanHash);
        }
        finally { Directory.Delete(root, true); }
    }
}
