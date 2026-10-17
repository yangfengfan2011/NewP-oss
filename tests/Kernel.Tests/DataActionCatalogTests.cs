using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
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
        // 原断言以固定 11 个 key 锁定真实目录顺序；模块新增或调整动作会让它漂移。
        // 现在从读取到的候选推导：目录按生效模块 ID 的序数序排列，各 owner 内部保持其声明顺序。
        var activeIds = original.Plan!.Modules.Select(module => module.ModuleId).ToHashSet(StringComparer.Ordinal);
        var ownerOrdered = candidates.Where(candidate => activeIds.Contains(candidate.ModuleId)).OrderBy(candidate => candidate.ModuleId, StringComparer.Ordinal).ToArray();
        var expectedKeys = ownerOrdered.SelectMany(candidate => candidate.DataActions).Select(action => action.Key).ToArray();
        Assert.NotEmpty(expectedKeys);
        Assert.Equal(expectedKeys, original.Plan.DataActionCatalog.Select(action => action.Key));
        Assert.All(original.Plan.DataActionCatalog.Where(action => new[] { "read", "create", "edit", "delete", "authorize" }.Contains(action.Key)), action => Assert.Equal("Authorization", action.ModuleId));
        foreach (var owner in new[] { "Document", "Part", "Workspace" })
        {
            var action = Assert.Single(original.Plan.DataActionCatalog, action => action.Key == owner.ToLowerInvariant() + "Copy");
            Assert.Equal(owner, action.ModuleId); Assert.True(action.RequiresRead); Assert.True(action.Enabled);
            Assert.Equal("data." + owner.ToLowerInvariant() + ".copy", action.BehaviorKey);
        }
        foreach (var owner in new[] { "Document", "Part" })
        {
            var action = Assert.Single(original.Plan.DataActionCatalog, action => action.Key == owner.ToLowerInvariant() + "ObjectCopy");
            Assert.Equal(owner, action.ModuleId); Assert.True(action.RequiresRead); Assert.True(action.Enabled);
            Assert.Equal("data." + owner.ToLowerInvariant() + ".objectCopy", action.BehaviorKey);
        }
        var download = Assert.Single(original.Plan.DataActionCatalog, action => action.Key == "download");
        Assert.Equal(new DataActionDeclaration("Document", "download", "下载", "data.download", RequiresRead: true), download);
        var reversed = compiler.Compile("order", candidates.Select(candidate =>
            candidate with { DataActions = candidate.DataActions.Reverse().ToArray() }));
        Assert.True(reversed.IsSuccess);
        // 反转各 owner 的贡献，不能越过模块边界反转整个合并目录。
        // 反转后的期望同样由候选推导：owner 顺序不变，只反转每个 owner 内部的贡献。
        var reversedKeys = ownerOrdered.SelectMany(candidate => candidate.DataActions.Reverse()).Select(action => action.Key).ToArray();
        Assert.False(expectedKeys.SequenceEqual(reversedKeys), "至少一个 owner 需贡献多个动作，否则无法证明顺序被跟踪。");
        Assert.Equal(reversedKeys, reversed.Plan!.DataActionCatalog.Select(action => action.Key));
        Assert.Equal(original.Plan.DataActionCatalog.Select(action => action.ModuleId), reversed.Plan.DataActionCatalog.Select(action => action.ModuleId));
        Assert.Equal(download, Assert.Single(reversed.Plan.DataActionCatalog, action => action.ModuleId == "Document" && action.Key == "download"));
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
