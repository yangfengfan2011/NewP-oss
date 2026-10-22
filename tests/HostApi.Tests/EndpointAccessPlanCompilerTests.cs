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
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Tests;

public sealed class EndpointAccessPlanCompilerTests
{
    [Fact]
    public void Compile_FeatureWithExistingHostGuard_IsOneBusinessRule()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapGet("/account", static () => "ok")
            .RequireAuthorization(new RequireFeatureAttribute("identity.account.read"), new AuthorizeAttribute("AdminOnly"));
        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
    }
    [Theory]
    [InlineData("anonymous")]
    [InlineData("authenticated")]
    [InlineData("denied")]
    [InlineData("feature")]
    public void Compile_FeatureRejectsAnotherTerminalRule(string kind)
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        var endpoint = app.MapGet("/conflict", static () => "never")
            .RequireAuthorization(new RequireFeatureAttribute("identity.account.read"), new AuthorizeAttribute("AdminOnly"));
        if (kind == "anonymous") endpoint.AllowAnonymous();
        else endpoint.RequireAuthorization(kind switch
        {
            "authenticated" => new AuthenticatedOnlyAttribute(),
            "denied" => new DenyAllAttribute("frozen"),
            _ => new RequireFeatureAttribute("identity.account.create")
        });
        var error = Assert.Throws<EndpointAccessPlanValidationException>(() =>
            EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources));
        Assert.Contains(error.Diagnostics, item => item.Code == EndpointAccessFailureCodes.RuleConflict);
    }

    [Fact]
    public void Compile_AcceptsPublicAuthenticatedDeniedAndFeatureEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapGet("/public", static () => "ok").AllowAnonymous();
        app.MapGet("/authenticated", static () => "ok").RequireAuthorization(new AuthenticatedOnlyAttribute());
        app.MapGet("/frozen", static () => "never").RequireAuthorization(new DenyAllAttribute("Frozen"));
        app.MapGet("/person", static () => "ok").RequireAuthorization(new RequireFeatureAttribute("organization.person.read"));
        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
    }
    [Fact]
    public void Compile_AggregatesMissingAndConflictingRulesInStableOrder()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapGet("/missing", static () => "missing");
        app.MapGet("/bare", static () => "bare").RequireAuthorization();
        app.MapGet("/conflict", static () => "conflict").AllowAnonymous().RequireAuthorization(new RequireFeatureAttribute("organization.person.read"));
        var error = Assert.Throws<EndpointAccessPlanValidationException>(() => EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources));
        Assert.Equal(new[] { EndpointAccessFailureCodes.RuleConflict, EndpointAccessFailureCodes.RuleMissing, EndpointAccessFailureCodes.RuleMissing }, error.Diagnostics.Select(item => item.Code));
    }
}
