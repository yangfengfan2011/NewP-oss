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
    public void ActualWorkspaceClipboardEndpoints_BindButtonsAndRetainTheSeparateObjectCopyStages()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-clipboard-coverage", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(WorkspaceContentsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var allEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        Type[] handlers = [typeof(WorkspaceClipboardSelectionHandler), typeof(WorkspacePasteReferencesHandler), typeof(WorkspaceRemoveReferencesHandler)];
        var buttonEndpoints = allEndpoints.Where(endpoint => handlers.Contains(endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()?.HandlerType)).ToArray();
        var buttons = compiled.Plan!.ActionDataAccessCatalog.Where(button => button.ModuleId == "Workspace"
            && button.ViewKey == "workspace.folder.list" && button.GetObjectRequirements().Count > 0
            && button.ActionKey is "copy" or "copySelected" or "cut" or "cutSelected" or "paste" or "remove" or "removeSelected").ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Contains(buttonEndpoints, endpoint => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        Assert.All(buttonEndpoints, endpoint => Assert.Contains(buttons, button => ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)));
        var endpoints = allEndpoints.Select(endpoint => endpoint.Metadata.GetMetadata<WorkspaceClipboardExecutionAttribute>()).Where(binding => binding is not null).ToArray();
        Assert.NotEmpty(endpoints);
        var contracts = compiled.Plan!.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Key.StartsWith("workspace.clipboard.", StringComparison.Ordinal)
                || contract.Key == WorkspaceObjectCopyExecutions.Copy).ToArray();
        Assert.NotEmpty(contracts);
        Assert.All(contracts, contract => Assert.Contains(endpoints, endpoint => endpoint!.Binding.Execution == contract.Key
            && endpoint.Binding.Provider == contract.Provider && contract.Requirements.Any(requirement => requirement.Stage == endpoint.Binding.Stage)));
        Assert.All(endpoints, endpoint => Assert.Contains(contracts, contract => contract.Key == endpoint!.Binding.Execution));
    }
}
