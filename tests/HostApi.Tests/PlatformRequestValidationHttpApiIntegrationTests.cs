using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Identity.Controllers;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PlatformRequestValidationHttpApiIntegrationTests
{
    [Theory]
    [InlineData("{\"ecode\":\"admin\",\"password\":\"do-not-leak\",")]
    [InlineData("{\"ecode\":[\"admin\"],\"password\":\"do-not-leak\"}")]
    public async Task AuthenticationLogin_InvalidJsonOrTypeUsesOptInProblemContract(string json)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddSingleton<ILocalCredentialAuthenticator, UnusedAuthenticator>();
        builder.Services.AddSingleton<IAuthenticationSessionRegistry, UnusedSessionRegistry>();
        builder.Services.AddSingleton<IPlatformTransactionExecutor, UnusedTransactionExecutor>();
        builder.Services.AddSingleton<IHostAuthenticationCookie, UnusedCookie>();
        builder.Services.AddSingleton(new PasswordVerificationGate(Options.Create(new HostAuthenticationOptions())));
        builder.Services.AddSingleton<IAuthorizationService, UnusedAuthorizationService>();

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("do-not-leak", responseBody, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(responseBody);
        Assert.Equal("REQUEST_VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.True(problem.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public void CurrentIdentityAuthenticationAndBindingControllersExplicitlyOptIn()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(AuthController), typeof(PlatformRequestValidationAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(IdentityUsersController), typeof(PlatformRequestValidationAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(IdentitySelfServiceController), typeof(PlatformRequestValidationAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(PersonUserBindingsController), typeof(PlatformRequestValidationAttribute)));
    }

    [Fact]
    public async Task UnmarkedLegacyControllerKeepsFrameworkValidationResponse()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(LegacyValidationProbeController).Assembly);

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/legacy-validation-probe", new { value = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("REQUEST_VALIDATION_FAILED", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed class UnusedAuthenticator : ILocalCredentialAuthenticator
    {
        public ValueTask<LocalCredentialAuthenticationResult> AuthenticateAsync(
            string accountCode,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSessionRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedTransactionExecutor : IPlatformTransactionExecutor
    {
        public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedCookie : IHostAuthenticationCookie
    {
        public string? Read(HttpContext context) => throw new NotSupportedException();
        public bool TryUnprotect(string protectedValue, out string credential) => throw new NotSupportedException();
        public void Append(HttpContext context, string credential, DateTime expiresAtUtc) => throw new NotSupportedException();
        public void Delete(HttpContext context) => throw new NotSupportedException();
    }

    private sealed class UnusedAuthorizationService : IAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

[ApiController]
[Route("api/legacy-validation-probe")]
public sealed class LegacyValidationProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] LegacyValidationProbeRequest request) => Ok();
}

public sealed record LegacyValidationProbeRequest([Range(1, 1)] int Value);
