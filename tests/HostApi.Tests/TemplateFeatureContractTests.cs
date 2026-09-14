using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Templates.BackendModuleTemplate;
using ICP.Platform.Templates.BackendModuleTemplate.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class TemplateFeatureContractTests
{
    [Fact]
    public async Task EmbeddedTemplateCapsule_BindsItsDeclaredReadPositionToTheRealStatusEndpoint()
    {
        var assembly = typeof(TemplateController).Assembly;
        using var stream = assembly.GetManifestResourceStream("ICP.Platform.Templates.BackendModuleTemplate.newp.plugin.json")!;
        using var capsule = JsonDocument.Parse(stream);
        var points = FeatureCatalogCompiler.Compile(FeatureCatalogCompiler.Read(capsule.RootElement, "TemplateModule"));
        var point = Assert.Single(points);
        Assert.Equal("template-module", point.Key);
        Assert.Equal("template-module.read", point.BehaviorKey);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        new TemplateModule().ConfigureServices(builder.Services);
        builder.Services.AddControllers().AddApplicationPart(assembly);
        builder.Services.AddAuthentication("TemplateTests").AddCookie("TemplateTests", options =>
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; });
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddSingleton<IFeatureDecisionSource>(new Source(point));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "template-user")], "TemplateTests"));
            await next();
        });
        app.UseAuthorization();
        app.MapControllers();
        FeatureEndpointBindings.Validate(((IEndpointRouteBuilder)app).DataSources, points);
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/template-module/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Get, "/api/template-module/status")).StatusCode);
    }

    private sealed class Source(FeaturePoint point) : IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(position == point.Position && behaviorKey == point.BehaviorKey ? PermissionDecision.Granted : PermissionDecision.Denied);
    }
}
