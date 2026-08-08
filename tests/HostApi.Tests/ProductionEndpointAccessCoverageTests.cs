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
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Controllers;
using ICP.Platform.Modules.MetadataConfigCenter;
using ICP.Platform.Modules.MetadataConfigCenter.Controllers;
using ICP.Platform.Modules.ModuleTemplateSample;
using ICP.Platform.Modules.ModuleTemplateSample.Controllers;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
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
            .AddApplicationPart(typeof(CompaniesController).Assembly);
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        using var app = builder.Build();
        app.MapControllers();
        app.MapStandardCrudResources();
        app.MapPluginManifestEndpoints("test-plugin-root");

        var registry = new ModuleRegistry();
        registry.RegisterModule(new AuditModule());
        registry.RegisterModule(new AuthorizationModule());
        registry.RegisterModule(new ConfigCenterModule());
        registry.RegisterModule(new DemoModule());
        registry.RegisterModule(new FilesModule());
        registry.RegisterModule(new MetadataConfigCenterModule());
        registry.RegisterModule(new ModuleTemplateSampleModule());
        registry.RegisterModule(new OrganizationModule());

        var plan = EndpointAccessPlanCompiler.Compile(
            ((IEndpointRouteBuilder)app).DataSources,
            registry);
        var manifestEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/plugins/",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Contains(
            plan.ActivePermissions,
            static permission => permission.Code == OrganizationPermissions.PersonDelete);
        Assert.Equal(3, manifestEndpoints.Length);
        Assert.All(
            manifestEndpoints,
            static endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }
}
