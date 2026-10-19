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
            .Select(endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>()).Where(item => item?.ResourceKey == "example.record").ToArray();
        Assert.Equal(new[] { "create", "delete", "export", "list", "read", "update" }, bindings.Select(item => item!.Operation).Order(StringComparer.Ordinal));
        Assert.Contains(bindings.Single(item => item!.Operation == "read")!.Bindings, binding => binding.Execution == "example.record.update" && binding.Stage == "prepare");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("legacy-switch")]
    [InlineData("missing-list")]
    [InlineData("missing-read")]
    [InlineData("missing-export")]
    public void ActualExampleEndpoints_RejectUnregisteredAndUnboundExecutions(string problem)
    {
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var bindings = descriptor.DataExecutions.ToDictionary(item => item.Key, item => item.Value);
        if (problem == "unknown") bindings["update"] = "unregistered.execution";
        else bindings.Remove(problem.StartsWith("missing-", StringComparison.Ordinal) ? problem[8..] : "update");
        descriptor = descriptor with { DataExecutions = bindings, LegacyDataExecutions = problem == "legacy-switch" ? ["update"] : [] };
        using var app = Build(descriptor);
        Assert.Throws<InvalidOperationException>(() => app.MapStandardCrudResources());
    }

    [Theory]
    [InlineData("api/identity/accounts", "list", "identity.account")]
    [InlineData("api/identity/accounts/{accountEid}", "read", "identity.account")]
    [InlineData("api/identity/accounts/{accountEid}/snapshot", "read", "identity.account")]
    [InlineData("api/authorization/roles/query", "list", "authorization.role")]
    [InlineData("api/authorization/roles/{roleId}", "read", "authorization.role")]
    public void ExistingMvcReads_HaveExactlyTheirOwnOperationAndRetainTheirOriginalQualification(string path, string operation, string resource)
    {
        using var app = Build(ExampleRecordCrudDescriptorFactory.Create());
        app.MapStandardCrudResources();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == path && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        var action = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()!;
        var declaration = Assert.Single(action.FilterDescriptors.Select(item => item.Filter).OfType<StandardCrudDataExecutionAttribute>());
        Assert.All(endpoint.Metadata.GetOrderedMetadata<StandardCrudDataExecutionAttribute>(), item =>
        {
            Assert.Equal(declaration.ResourceKey, item.ResourceKey); Assert.Equal(declaration.Operation, item.Operation);
            Assert.Equal(declaration.Bindings, item.Bindings);
        });
        Assert.Equal(operation, declaration.Operation); Assert.Equal(resource, declaration.ResourceKey);
        var binding = Assert.Single(declaration.Bindings);
        Assert.Equal(resource + "." + operation, binding.Execution); Assert.Equal("standardCrud." + operation, binding.Provider);
        Assert.Equal(resource + ".read", Assert.Single(endpoint.Metadata.GetOrderedMetadata<ICP.Platform.Authorization.Http.RequireFeatureAttribute>()).BehaviorKey);
        if (resource == "identity.account")
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>(), item => item.Policy == "AdminOnly");
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
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Organization.Crud.PersonCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Organization.Crud.CompanyCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Organization.Crud.DepartmentCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Organization.Crud.PositionCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Part.PartCrudDescriptor.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Document.DocumentCrudDescriptor.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Identity.Application.IdentityAccountCrudResource.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Authorization.Services.RoleReadResource.Create());
        builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Identity.Controllers.IdentityAccountsController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Authorization.Controllers.RoleController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
