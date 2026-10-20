using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceFolderEditMemberTests
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void FolderEdit_UsesOnlyButtonRequirementsAndPreservesPreparationReadSource()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("folder-edit-button-327", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var edit = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Workspace"
            && item.ActionKey == "edit" && item.Surface == "rowActions");
        Assert.Empty(edit.Execution);
        Assert.Equal("workspace.folder.editObject", edit.BusinessBinding);
        Assert.Equal(["Document", "Part", "WorkspaceFolder"], edit.ObjectRequirements.Select(item => item.Object).Order().ToArray());
        Assert.All(edit.ObjectRequirements, item => Assert.Equal(["edit"], item.Actions));
        var workspace = plan.Modules.Single(item => item.ModuleId == "Workspace");
        Assert.DoesNotContain(workspace.DataExecutions, item => item.Key is "workspace.content.edit" or "workspace.folder.edit.target"
            or "workspace.content.delete" or "workspace.content.delete.target");
        var read = Assert.Single(workspace.DataExecutions, item => item.Key == "workspace.editValues.detail");
        Assert.Equal(["read"], Assert.Single(Assert.Single(read.Requirements).AnyOf));
        foreach (var module in plan.Modules.Where(item => item.ModuleId is "Part" or "Document"))
            Assert.DoesNotContain(module.DataExecutions, item => item.Key.EndsWith(".update", StringComparison.Ordinal)
                || item.Key.EndsWith(".delete", StringComparison.Ordinal));
    }
}
