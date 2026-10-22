using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationLifecycleHttpApiIntegrationTests
{
    [Fact]
    public async Task FormalStatusRoute_RejectsAnonymousRequestAndUsesAuthenticatedActor()
    {
        var service = new RecordingLifecycleService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PersonLifecycleController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(
            "Feature:organization.person.disable",
            policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddSingleton<IPersonLifecycleService>(service);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var anonymous = await client.PutAsJsonAsync(
            "/api/organization/persons/Person_1/status/disable",
            new SetPersonLifecycleStatusRequest(false, "停用"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(0, service.CallCount);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/organization/persons/Person_1/status/disable")
        {
            Content = JsonContent.Create(new SetPersonLifecycleStatusRequest(false, "停用"))
        };
        request.Headers.Add("X-Test-User", "User_Admin");
        var authenticated = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Equal("User_Admin", service.Actor?.ActorId);
        Assert.False(string.IsNullOrWhiteSpace(service.Actor?.TraceId));
        using var response = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
        Assert.False(response.RootElement.TryGetProperty("revision", out _));
    }

    [Theory]
    [InlineData("{", "$")]
    [InlineData("{\"isEnabled\":\"not-a-boolean\",\"reason\":\"停用\"}", "$.isEnabled")]
    public async Task FormalStatusRoute_InvalidPayloadUsesPlatformValidationProblem(
        string payload,
        string expectedErrorField)
    {
        var service = new RecordingLifecycleService();
        await using var app = await BuildApplicationAsync(service);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/organization/persons/Person_1/status/disable")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-User", "User_Admin");

        using var response = await app.GetTestClient().SendAsync(request);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("REQUEST_VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(expectedErrorField, out _));
        Assert.Equal(0, service.CallCount);
    }

    private static async Task<WebApplication> BuildApplicationAsync(RecordingLifecycleService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PersonLifecycleController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(
            "Feature:organization.person.disable",
            policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddSingleton<IPersonLifecycleService>(service);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingLifecycleService : IPersonLifecycleService
    {
        public int CallCount { get; private set; }
        public PersonLifecycleActor? Actor { get; private set; }

        public ValueTask<PersonLifecycleDto> SetStatusAsync(
            string personEid,
            SetPersonLifecycleStatusRequest request,
            PersonLifecycleActor actor,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Actor = actor;
            return ValueTask.FromResult(new PersonLifecycleDto(personEid, request.IsEnabled, "User_1", null));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OrganizationLifecycleApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var accountEid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, accountEid.ToString())],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
