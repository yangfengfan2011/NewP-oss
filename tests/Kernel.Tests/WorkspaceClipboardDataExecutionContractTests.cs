using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspaceClipboardDataExecutionContractTests
{
    [Fact]
    public void ActualWorkspaceClipboard_CompilesOneFloorForEveryListAndTreeOccurrence()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-clipboard-contract", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var owner = candidates.Single(candidate => candidate.ModuleId == "Workspace");
        var contracts = owner.DataExecutions.Where(contract => contract.Key.StartsWith("workspace.clipboard.", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(contracts);
        Assert.All(contracts, contract => Assert.False(contract.LegacyActions));
        var declarations = compiled.Plan!.ActionDataAccessCatalog.Where(item => item.Execution.StartsWith("workspace.clipboard.", StringComparison.Ordinal)).ToArray();
        Assert.All(contracts, contract => Assert.Contains(declarations, declaration => declaration.Execution == contract.Key));
        Assert.All(owner.Features.SelectMany(feature => feature.DataAccess).Where(item => item.Execution.StartsWith("workspace.clipboard.", StringComparison.Ordinal)),
            declaration => Assert.Empty(declaration.Actions));
        Assert.DoesNotContain(declarations, declaration => declaration.ActionKey == "clearClipboard");
        foreach (var declaration in declarations)
        {
            Assert.Equal("Workspace", declaration.ModuleId);
            Assert.Equal("workspace.backend", declaration.Provider);
            Assert.NotNull(declaration.Requirements);
            Assert.Contains("workspace.folder", declaration.Resources);
            Assert.Contains("part.part", declaration.Resources);
            Assert.Contains("document.document", declaration.Resources);
        }
        foreach (var key in new[] { "copy", "cut", "remove" })
            Assert.Contains(Assert.Single(declarations, declaration => declaration.Surface == "rowActions" && declaration.ActionKey == key).FeaturePositions,
                position => compiled.Plan.FeatureCatalog.Any(point => point.Position == position && point.ActionScope == "contextMenu"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var allDeclarations = compiled.Plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace").ToArray();
        // 同一正式fixture也覆盖已存在树动作的目标读取来源，位置仍取实际编译目录。
        var clipboardKeys = new[] { "copy", "copySelected", "cut", "cutSelected", "paste", "remove", "removeSelected", "clearClipboard", "edit", "moveUp", "moveDown" };
        var features = compiled.Plan.FeatureCatalog.Where(point => point.ModuleId == "Workspace"
            && (point.Kind == "navigation" && point.Key == "workspace" || point.Kind == "action" && clipboardKeys.Contains(point.Key))).ToArray();
        var json = JsonSerializer.Serialize(new { declarations = allDeclarations, features }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_WORKSPACE_CLIPBOARD_CONTRACT_FIXTURE") == "1")
        {
            File.WriteAllText(Path.Combine(root!.FullName, ".codex-temp", "issue-305", "workspace-clipboard-data-access.json"), json);
            return;
        }
        var fixture = Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "WorkspaceClipboardDataAccess.contract.json");
        Assert.True(File.Exists(fixture));
        Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(fixture))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }
}
