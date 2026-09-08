using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Tests;

public sealed class EndpointAccessPlanCompilerTests
{
    [Fact]
    public void Compile_AcceptsExactlyOneTerminalRuleAndKnownOwnedPermission()
    {
        var registry = Registry(new PermissionModule(
            "Organization",
            Permission("Organization.Person.View", "Organization")));
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapGet("/public", static () => "ok").AllowAnonymous();
        app.MapGet("/authenticated", static () => "ok")
            .RequireAuthorization(new AuthenticatedOnlyAttribute());
        app.MapGet("/frozen", static () => "never")
            .RequireAuthorization(new DenyAllAttribute("Frozen until a later batch."));
        app.MapGet("/person", static () => "ok")
            .WithMetadata(new EndpointModuleAttribute("Organization"))
            .RequirePermission("Organization.Person.View");

        var plan = EndpointAccessPlanCompiler.Compile(
            ((IEndpointRouteBuilder)app).DataSources,
            registry);

        Assert.Equal(
            ["Organization.Person.View"],
            plan.ActivePermissions.Select(static permission => permission.Code));
    }

    [Fact]
    public void Compile_RealOrganizationTreeController_HasOneTerminalRulePerRead()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(OrganizationTreeAccess.Configure);
        builder.Services.AddControllers().AddApplicationPart(typeof(OrganizationTreeController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/organization/tree/", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(4, endpoints.Length);
        var policy = app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(OrganizationTreeAccess.PolicyName)!;
        Assert.Equal(new[] { "Organization.Company.View", "Organization.Department.View" },
            policy.Requirements.OfType<PermissionAuthorizationRequirement>().Select(requirement => requirement.Requirement.PermissionCode));
        EndpointAccessPlanCompiler.Compile([new DefaultEndpointDataSource(endpoints)], Registry(new PermissionModule("Organization",
            Permission("Organization.Company.View", "Organization"), Permission("Organization.Department.View", "Organization"))));
    }

    [Fact]
    public void Compile_AggregatesRulesAndCatalogFailuresInStableOrder()
    {
        var registry = Registry(
            new PermissionModule(
                "Organization",
                Permission("Organization.Person.View", "Organization")),
            new PermissionModule(
                "Collision",
                Permission("organization.person.view", "Collision")));
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapGet("/missing", static () => "missing");
        app.MapGet("/conflict", static () => "conflict")
            .AllowAnonymous()
            .WithMetadata(new EndpointModuleAttribute("Organization"))
            .RequirePermission("Organization.Person.View");
        app.MapGet("/unknown", static () => "unknown")
            .WithMetadata(new EndpointModuleAttribute("Organization"))
            .RequirePermission("Organization.Person.Unknown");
        app.MapGet("/wrong-owner", static () => "wrong")
            .WithMetadata(new EndpointModuleAttribute("Collision"))
            .RequirePermission("Organization.Person.View");

        var exception = Assert.Throws<EndpointAccessPlanValidationException>(() =>
            EndpointAccessPlanCompiler.Compile(
                ((IEndpointRouteBuilder)app).DataSources,
                registry));

        Assert.Equal(
            [
                EndpointAccessFailureCodes.RuleConflict,
                EndpointAccessFailureCodes.RuleMissing,
                EndpointAccessFailureCodes.PermissionCodeCollision,
                EndpointAccessFailureCodes.PermissionModuleMismatch,
                EndpointAccessFailureCodes.PermissionUnknown
            ],
            exception.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct());
    }

    private static ModuleRegistry Registry(params IModule[] modules)
    {
        var registry = new ModuleRegistry();
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        return registry;
    }

    private static PermissionDescriptor Permission(string code, string moduleId) => new()
    {
        Code = code,
        DisplayName = code,
        ModuleId = moduleId
    };

    private sealed class PermissionModule : ModuleBase, IPermissionProvider
    {
        private readonly IReadOnlyList<PermissionDescriptor> _permissions;

        public PermissionModule(string moduleId, params PermissionDescriptor[] permissions)
        {
            ModuleId = moduleId;
            _permissions = permissions;
        }

        public override string ModuleId { get; }

        public override string DisplayName => ModuleId;

        public IReadOnlyList<PermissionDescriptor> GetPermissions() => _permissions;
    }
}
