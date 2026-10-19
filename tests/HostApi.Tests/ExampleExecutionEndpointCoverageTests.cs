using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Example.Crud;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ExampleExecutionEndpointCoverageTests
{
    [Fact]
    public void ActualExampleEndpoints_BindEveryMigratedExecution()
    {
        using var app = Build(ExampleRecordCrudDescriptorFactory.Create());
        app.MapStandardCrudResources();
        var bindings = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>()).Where(item => item is not null).ToArray();
        Assert.Equal(new[] { "create", "delete", "list", "read", "update" }, bindings.Select(item => item!.Operation).Order(StringComparer.Ordinal));
        Assert.Contains(bindings.Single(item => item!.Operation == "read")!.Bindings, binding => binding.Execution == "example.record.update" && binding.Stage == "prepare");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("legacy-switch")]
    public void ActualExampleEndpoints_RejectUnregisteredAndUnboundExecutions(string problem)
    {
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var bindings = descriptor.DataExecutions.ToDictionary(item => item.Key, item => item.Value);
        if (problem == "unknown") bindings["update"] = "unregistered.execution";
        else bindings.Remove("update");
        descriptor = descriptor with { DataExecutions = bindings, LegacyDataExecutions = problem == "legacy-switch" ? ["update"] : [] };
        using var app = Build(descriptor);
        Assert.Throws<InvalidOperationException>(() => app.MapStandardCrudResources());
    }

    private static WebApplication Build(StandardCrudDescriptor descriptor)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("execution-coverage", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(compiled.Plan!);
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddStandardCrudResource(descriptor);
        return builder.Build();
    }
}
