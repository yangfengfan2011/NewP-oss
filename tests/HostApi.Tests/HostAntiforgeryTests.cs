using System.Net;
using System.Net.Http.Json;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.HostApi.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class HostAntiforgeryTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task CookieWriteWithoutToken_FailsBeforeBusinessExecution(string method)
    {
        await using var scenario = await Scenario.StartAsync();

        using var response = await scenario.Client.SendAsync(
            CreateCookieWrite(new HttpMethod(method), "/api/protected-write"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(HostAntiforgeryFailureCodes.InvalidToken, problem?["code"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem?["traceId"].ToString()));
        Assert.Equal(0, scenario.BusinessExecutions);
    }

    [Fact]
    public async Task LoginWithInvalidToken_FailsBeforeBusinessExecution()
    {
        await using var scenario = await Scenario.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add(HostAntiforgeryDefaults.HeaderName, "invalid-token");

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, scenario.BusinessExecutions);
    }

    [Fact]
    public async Task TokenEndpoint_IssuesTokenThatAllowsExactlyOneBusinessExecution()
    {
        await using var scenario = await Scenario.StartAsync();

        using var tokenResponse = await scenario.Client.GetAsync("/api/auth/antiforgery-token");
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        var cookie = Assert.Single(tokenResponse.Headers.GetValues("Set-Cookie"))
            .Split(';', 2)[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/protected-write");
        request.Headers.Add("Cookie", $"{cookie}; Test.Auth=fake-credential");
        request.Headers.Add(HostAntiforgeryDefaults.HeaderName, token!.RequestToken);

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, scenario.BusinessExecutions);
    }

    private sealed record TokenResponse(string RequestToken);

    private static HttpRequestMessage CreateCookieWrite(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", "Test.Auth=fake-credential");
        return request;
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private int _businessExecutions;

        private Scenario(WebApplication application)
        {
            _application = application;
        }

        public HttpClient Client { get; private set; } = null!;

        public int BusinessExecutions => Volatile.Read(ref _businessExecutions);

        public static async Task<Scenario> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development"
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddHostAntiforgery(builder.Environment);
            builder.Services.AddSingleton<IHostAuthenticationCookie, TestAuthenticationCookie>();
            var application = builder.Build();
            application.UseHostAntiforgery();
            var scenario = new Scenario(application);
            application.MapGet("/api/auth/antiforgery-token", (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                return Results.Json(new TokenResponse(tokens.RequestToken!));
            });
            application.MapMethods(
                "/api/protected-write",
                ["POST", "PUT", "PATCH", "DELETE"],
                () =>
                {
                    Interlocked.Increment(ref scenario._businessExecutions);
                    return Results.NoContent();
                });
            application.MapPost("/api/auth/login", () =>
            {
                Interlocked.Increment(ref scenario._businessExecutions);
                return Results.NoContent();
            });
            await application.StartAsync();
            scenario.Client = application.GetTestClient();
            return scenario;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class TestAuthenticationCookie : IHostAuthenticationCookie
    {
        public string? Read(HttpContext context) => context.Request.Cookies["Test.Auth"];

        public bool TryUnprotect(string protectedValue, out string credential)
        {
            credential = protectedValue;
            return true;
        }

        public void Append(HttpContext context, string credential, DateTime expiresAtUtc) =>
            throw new NotSupportedException();

        public void Delete(HttpContext context) => throw new NotSupportedException();
    }
}
