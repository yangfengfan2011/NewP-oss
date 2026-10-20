using System.Text.Json;
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
    public void ActualWorkspaceClipboardEndpoints_BindEveryMigratedExecutionAndStage()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-clipboard-coverage", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(WorkspaceContentsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<WorkspaceClipboardExecutionAttribute>()).Where(binding => binding is not null).ToArray();
        Assert.NotEmpty(endpoints);
        var contracts = compiled.Plan!.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Key.StartsWith("workspace.clipboard.", StringComparison.Ordinal)
                || contract.Key == WorkspaceObjectCopyExecutions.Part).ToArray();
        Assert.NotEmpty(contracts);
        Assert.All(contracts, contract => Assert.Contains(endpoints, endpoint => endpoint!.Binding.Execution == contract.Key
            && endpoint.Binding.Provider == contract.Provider && contract.Requirements.Any(requirement => requirement.Stage == endpoint.Binding.Stage)));
        Assert.All(endpoints, endpoint => Assert.Contains(contracts, contract => contract.Key == endpoint!.Binding.Execution));
    }
}
