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
    public void ActualWorkspaceClipboardEndpoints_BindButtonsAndRetainOnlyUnmigratedFolderCopyContracts()
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
            typeof(WorkspaceObjectCopyPreparationHandler), typeof(WorkspaceObjectCopyHandler), typeof(WorkspaceObjectCopyResultHandler)];
        var buttonEndpoints = allEndpoints.Where(endpoint => handlers.Contains(endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()?.HandlerType)).ToArray();
        var buttons = compiled.Plan!.ActionDataAccessCatalog.Where(button => button.ModuleId == "Workspace"
            && button.ViewKey == "workspace.folder.list"
            && button.ActionKey is "copy" or "copySelected" or "cut" or "cutSelected" or "paste" or "remove" or "removeSelected" or "pasteObjectCopy" or "resolveObjectCopy").ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Contains(buttonEndpoints, endpoint => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        Assert.All(buttonEndpoints, endpoint => Assert.Contains(buttons, button => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        // Folder副本仍由原实际方法固定prepare/submit来源；它们没有挂载已退役的剪贴板filter。
        var folderContracts = compiled.Plan!.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Key is WorkspaceFolderCopyExecutions.Shallow or WorkspaceFolderCopyExecutions.Structure).ToArray();
        Assert.Equal(2, folderContracts.Length);
        foreach (var contract in folderContracts)
        {
            Assert.Contains(contract.Requirements, requirement => requirement.Stage == "prepare");
            Assert.Contains(contract.Requirements, requirement => requirement.Stage == "submit");
            var declarations = compiled.Plan.ActionDataAccessCatalog.Where(item => item.Execution == contract.Key).ToArray();
            Assert.NotEmpty(declarations);
            var endpoints = allEndpoints.Where(endpoint => contract.FeatureBehaviors.Contains(
                endpoint.Metadata.GetMetadata<RequireFeatureAttribute>()?.BehaviorKey ?? "")).ToArray();
            Assert.Equal(2, endpoints.Length);
            Assert.Contains(endpoints.Cast<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText!.EndsWith("/prepare", StringComparison.Ordinal));
            Assert.Contains(endpoints.Cast<RouteEndpoint>(), endpoint => !endpoint.RoutePattern.RawText!.EndsWith("/prepare", StringComparison.Ordinal));
        }
    }
}
