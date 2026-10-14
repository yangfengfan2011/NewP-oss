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
        Assert.Equal(new[] { "read", "edit" }, catalog.Select(action => action.Key));
        Assert.True(catalog.Single(action => action.Key == "read").Enabled);
        Assert.False(catalog.Single(action => action.Key == "edit").Enabled);
        Assert.Throws<InvalidOperationException>(() => DataActionCatalogCompiler.Compile(declarations.Concat(declarations)));
    }

    [Fact]
    public void FormalPlan_PreservesRealActionOrderAndTracksReordering()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiler = new CanonicalModulePlanCompiler();
        var original = compiler.Compile("order", candidates);
        Assert.True(original.IsSuccess);
        Assert.Equal(new[] { "read", "create", "edit", "delete", "authorize", "download" },
            original.Plan!.DataActionCatalog.Select(action => action.Key));
        Assert.All(original.Plan.DataActionCatalog.Where(action => action.Key != "download"), action => Assert.Equal("Authorization", action.ModuleId));
        var download = Assert.Single(original.Plan.DataActionCatalog, action => action.Key == "download");
        Assert.Equal(new DataActionDeclaration("Document", "download", "下载", "data.download", RequiresRead: true), download);
        var reversed = compiler.Compile("order", candidates.Select(candidate =>
            candidate with { DataActions = candidate.DataActions.Reverse().ToArray() }));
        Assert.True(reversed.IsSuccess);
        // 反转各 owner 的贡献，不能越过模块边界反转整个合并目录。
        Assert.Equal(new[] { "authorize", "delete", "edit", "create", "read", "download" },
            reversed.Plan!.DataActionCatalog.Select(action => action.Key));
        Assert.Equal(original.Plan.DataActionCatalog.Select(action => action.ModuleId), reversed.Plan.DataActionCatalog.Select(action => action.ModuleId));
        Assert.Equal(download, Assert.Single(reversed.Plan.DataActionCatalog, action => action.ModuleId == "Document"));
        Assert.NotEqual(original.Plan.PlanHash, reversed.Plan.PlanHash);
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
