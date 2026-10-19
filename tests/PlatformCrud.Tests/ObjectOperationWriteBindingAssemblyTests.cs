using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed partial class ObjectOperationExtensionAssemblyTests
{
    [Theory]
    [InlineData("valid", null)]
    [InlineData("unknown-resource", "OBJECT_OPERATION_DATA_EXECUTION_BINDING_INVALID")]
    [InlineData("wrong-owner", "OBJECT_OPERATION_DATA_EXECUTION_BINDING_INVALID")]
    [InlineData("unsupported-action", "OBJECT_OPERATION_DATA_EXECUTION_BINDING_INVALID")]
    [InlineData("unknown-caller", "OBJECT_OPERATION_DATA_EXECUTION_CALLER_INVALID")]
    public async Task StartAsync_InternalWriteContractRequiresActualBehaviorOwnerActionAndCaller(string scenario, string? expected)
    {
        var resource = scenario == "unknown-resource" ? "organization.missing" : "organization.person";
        var caller = typeof(ObjectOperationExtensionAssemblyTests).FullName + "." + (scenario == "unknown-caller" ? "MissingMethod" : "PersonDefinition");
        var contract = new DataExecutionContract("internal.person.create", scenario == "wrong-owner" ? "MissingOwner" : "Organization",
            scenario == "unsupported-action" ? "standardCrud.delete" : "standardCrud.create", "internal", [],
            [new("candidate", "submit", "candidate", [resource], [["create"]])])
        { FixedBindings = [new("internal", "internal.person.create", Caller: caller)] };
        var original = new CanonicalModulePlanCompiler().Compile("internal-write-owner", [Capsule("Organization")]).Plan!;
        var plan = original with { Modules = original.Modules.Select(module => module with { DataExecutions = [contract] }).ToArray() };
        using var host = BuildHost(services =>
        {
            services.AddSingleton(plan);
            services.AddSingleton<InvocationLog>();
            services.AddObjectBehavior<RecordingBehavior>(new TestModule("Organization", []), PersonDefinition());
        });
        if (expected is null)
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.Equal(expected, failure.Message);
        }
    }
}
