using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspaceClipboardDataExecutionContractTests
{
    [Fact]
    public void ActualWorkspaceClipboard_BindsTheFormalParentForEveryListAndTreeOccurrence()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-clipboard-contract", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var owner = candidates.Single(candidate => candidate.ModuleId == "Workspace");
        var contracts = owner.DataExecutions.Where(contract => contract.Key.StartsWith("workspace.clipboard.", StringComparison.Ordinal)).ToArray();
        Assert.Empty(contracts);
        Assert.DoesNotContain(owner.Features.SelectMany(feature => feature.DataAccess), declaration => declaration.Execution.StartsWith("workspace.clipboard.", StringComparison.Ordinal));
        string[] bindings = ["workspace.folder.copy", "workspace.folder.cut", "workspace.folder.paste", "workspace.folder.remove", "workspace.folder.removeSelected"];
        var declarations = compiled.Plan!.ActionDataAccessCatalog.Where(item => bindings.Contains(item.BusinessBinding)
            && item.GetObjectRequirements().Count > 0).ToArray();
        Assert.Equal(7, declarations.Length);
        foreach (var declaration in declarations)
        {
            Assert.Equal("Workspace", declaration.ModuleId);
            Assert.Empty(declaration.Execution);
            Assert.Null(declaration.Provider);
            Assert.Null(declaration.Requirements);
            Assert.Equal("WorkspaceFolder", declaration.Object);
            Assert.Equal(["workspace.folder"], declaration.Resources);
            Assert.Equal([declaration.BusinessBinding == "workspace.folder.copy" ? "read" : "edit"], declaration.Actions);
        }
        var clear = Assert.Single(compiled.Plan.ActionDataAccessCatalog, declaration => declaration.ModuleId == "Workspace" && declaration.ActionKey == "clearClipboard");
        Assert.Empty(clear.GetObjectRequirements());
        foreach (var key in new[] { "copy", "cut", "remove" })
            Assert.Contains(Assert.Single(declarations, declaration => declaration.Surface == "rowActions" && declaration.ActionKey == key).FeaturePositions,
                position => compiled.Plan.FeatureCatalog.Any(point => point.Position == position && point.ActionScope == "contextMenu"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        // 真实消费者fixture同时保留旧读取/动作与已迁移按钮；两种权限语义分别由各自断言拥有。
        var allDeclarations = compiled.Plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace").ToArray();
        var reorder = allDeclarations.Where(item => item.BusinessBinding == "workspace.folder.reorder").ToArray();
        Assert.Equal(2, reorder.Length);
        Assert.All(reorder, item => { Assert.Equal("WorkspaceFolder", item.Object); Assert.Equal(new[] { "edit" }, item.Actions); Assert.Null(item.Requirements); });
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key == "workspace.folder.reorder");
        foreach (var (action, model) in new[] { ("create", "WorkspaceFolder"), ("createPart", "Part"), ("createDocument", "Document") })
        {
            var item = Assert.Single(allDeclarations, item => item.Surface == "pageActions" && item.ActionKey == action);
            Assert.Empty(item.Execution); Assert.Equal(model, item.Object); Assert.Equal(["create"], item.Actions);
            Assert.Equal("workspace.folder." + action, item.BusinessBinding);
        }
        foreach (var (surface, action, required) in new[] { ("rowActions", "edit", "edit"), ("rowActions", "delete", "delete"), ("pageActions", "deleteObjects", "delete") })
        {
            var item = Assert.Single(allDeclarations, item => item.Surface == surface && item.ActionKey == action);
            Assert.Empty(item.Execution); Assert.Null(item.Object);
            Assert.NotNull(item.ObjectRequirements);
            Assert.Equal(["Document", "Part", "WorkspaceFolder"], item.ObjectRequirements.Select(target => target.Object).Order());
            Assert.All(item.ObjectRequirements, target => Assert.Equal([required], target.Actions));
        }
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key == "workspace.content.createDocument");
        foreach (var action in new[] { "pasteFolderCopy", "pasteFolderStructure" })
        {
            var item = Assert.Single(allDeclarations, item => item.Surface == "pageActions" && item.ActionKey == action);
            Assert.Empty(item.Execution); Assert.Null(item.Provider); Assert.Null(item.Requirements);
            Assert.Equal("WorkspaceFolder", item.Object); Assert.Equal(["workspaceCopy"], item.Actions);
            Assert.Equal("workspace.folder." + action, item.BusinessBinding);
        }
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key is "workspace.folder.shallowCopy" or "workspace.folder.shallowCopy.directTarget"
            or "workspace.folder.structureCopy" or "workspace.folder.structureCopy.node");
        // 同一正式fixture也覆盖已存在树动作的目标读取来源，位置仍取实际编译目录。
        var clipboardKeys = new[] { "create", "copy", "copySelected", "cut", "cutSelected", "paste", "remove", "removeSelected", "clearClipboard", "edit", "moveUp", "moveDown", "createPart", "createDocument", "delete", "deleteObjects", "pasteFolderCopy", "pasteFolderStructure", "pasteObjectCopy", "resolveObjectCopy" };
        var features = compiled.Plan.FeatureCatalog.Where(point => point.ModuleId == "Workspace"
            && (point.Kind == "navigation" && point.Key == "workspace" || point.Kind == "action" && clipboardKeys.Contains(point.Key))).ToArray();
        var json = JsonSerializer.Serialize(new { declarations = allDeclarations, features }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var fixture = Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "WorkspaceClipboardDataAccess.contract.json");
        if (Environment.GetEnvironmentVariable("NEWP_WORKSPACE_CLIPBOARD_CONTRACT_FIXTURE") == "1")
            File.WriteAllText(fixture, json.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.True(File.Exists(fixture));
        Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(fixture))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }
}
