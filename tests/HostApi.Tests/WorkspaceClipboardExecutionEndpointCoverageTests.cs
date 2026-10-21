using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspaceClipboardExecutionEndpointCoverageTests
{
    [Fact]
    public void ActualWorkspaceClipboardAndFolderCopyEndpoints_BindTheirOwnButtonsWithoutRetiredContracts()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-clipboard-coverage", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(WorkspaceContentsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var allEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        Type[] handlers = [typeof(WorkspaceClipboardSelectionHandler), typeof(WorkspacePasteReferencesHandler), typeof(WorkspaceRemoveReferencesHandler),
            typeof(WorkspaceObjectCopyPreparationHandler), typeof(WorkspaceObjectCopyHandler), typeof(WorkspaceObjectCopyResultHandler),
            typeof(WorkspaceFolderCopyPreparationHandler), typeof(WorkspaceFolderCopyHandler)];
        var buttonEndpoints = allEndpoints.Where(endpoint => handlers.Contains(endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()?.HandlerType)).ToArray();
        var buttons = compiled.Plan!.ActionDataAccessCatalog.Where(button => button.ModuleId == "Workspace"
            && button.ViewKey == "workspace.folder.list"
            && button.ActionKey is "copy" or "copySelected" or "cut" or "cutSelected" or "paste" or "remove" or "removeSelected" or "pasteObjectCopy" or "resolveObjectCopy" or "pasteFolderCopy" or "pasteFolderStructure").ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Contains(buttonEndpoints, endpoint => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        Assert.All(buttonEndpoints, endpoint => Assert.Contains(buttons, button => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        // 两类Folder副本各自声明实际源用途；父、直接内容、候选和创建事实由原业务时点验证。
        var folderButtons = buttons.Where(button => button.ActionKey is "pasteFolderCopy" or "pasteFolderStructure").ToArray();
        Assert.Equal(2, folderButtons.Length);
        Assert.All(folderButtons, button =>
        {
            var requirement = Assert.Single(button.GetObjectRequirements());
            Assert.Equal("WorkspaceFolder", requirement.Object);
            Assert.Equal(new[] { "workspaceCopy" }, requirement.Actions);
        });
        Assert.DoesNotContain(compiled.Plan.Modules.SelectMany(module => module.DataExecutions), contract =>
            contract.Key is "workspace.folder.shallowCopy" or "workspace.folder.shallowCopy.directTarget"
                or "workspace.folder.structureCopy" or "workspace.folder.structureCopy.node");
    }
}
