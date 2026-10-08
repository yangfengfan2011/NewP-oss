using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Endpoints;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Modules.Audit;
using ICP.Platform.Modules.Audit.Controllers;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Controllers;
using ICP.Platform.Modules.ConfigCenter;
using ICP.Platform.Modules.ConfigCenter.Controllers;
using ICP.Platform.Modules.Demo;
using ICP.Platform.Modules.Example;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Controllers;
using ICP.Platform.Modules.MetadataConfigCenter;
using ICP.Platform.Modules.MetadataConfigCenter.Controllers;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Controllers;
using ICP.Platform.Modules.ModuleTemplateSample;
using ICP.Platform.Modules.ModuleTemplateSample.Controllers;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ProductionEndpointAccessCoverageTests
{
    [Fact]
    public void Compile_AllCurrentProductionControllersPersonCrudAndPluginManifests_HaveTerminalRules()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddApplicationPart(typeof(AccessLogsController).Assembly)
            .AddApplicationPart(typeof(RoleController).Assembly)
            .AddApplicationPart(typeof(ConfigController).Assembly)
            .AddApplicationPart(typeof(FilesController).Assembly)
            .AddApplicationPart(typeof(MetadataModulesController).Assembly)
            .AddApplicationPart(typeof(SampleStatusController).Assembly)
            .AddApplicationPart(typeof(IdentityAccountsController).Assembly)
            .AddApplicationPart(typeof(PersonOrganizationLookupsController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceSpacesController).Assembly)
            .AddApplicationPart(typeof(ExampleModule).Assembly);
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ExampleRecordCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Part.PartCrudDescriptor.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Document.DocumentCrudDescriptor.Create());
        using var app = builder.Build();
        app.MapControllers();
        app.MapStandardCrudResources();
        app.MapPluginManifestEndpoints("test-plugin-root");

        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
        var partEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/part/parts", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(new[] { "part.part.create", "part.part.read" }, partEndpoints.SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
            .Select(feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(partEndpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Any(method => method is "PUT" or "DELETE")),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<DenyAllAttribute>()));
        var documentEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/document/documents", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(new[] { "document.document.create", "document.document.read" }, documentEndpoints.SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
            .Select(feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(documentEndpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Any(method => method is "PUT" or "DELETE")),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<DenyAllAttribute>()));
        var manifestEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/plugins/",
                StringComparison.Ordinal) == true)
            .ToArray();
        var exampleRecordEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/example/records",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(9, exampleRecordEndpoints.Length);
        Assert.All(
            exampleRecordEndpoints,
            static endpoint =>
            {
                Assert.Equal(
                    "Example",
                    Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>()).ModuleId);
                Assert.Equal(1, endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>().Count
                    + endpoint.Metadata.GetOrderedMetadata<DenyAllAttribute>().Count);
            });
        Assert.Equal(
            ["example.record.create", "example.record.delete", "example.record.export", "example.record.read", "example.record.update"],
            exampleRecordEndpoints.SelectMany(static endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
                .Select(static feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(4, manifestEndpoints.Length);
        Assert.Contains(
            manifestEndpoints,
            static endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/plugins/active-module-runtime.json",
                StringComparison.Ordinal));
        Assert.All(
            manifestEndpoints,
            static endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }
}
