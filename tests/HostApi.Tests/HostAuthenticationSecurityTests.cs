using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Authentication.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class HostAuthenticationSecurityTests
{
    [Fact]
    public void ProductionOptions_RejectMissingOrInstallLocalUnprotectedKeyRing()
    {
        var validator = new HostAuthenticationOptionsValidator(new TestEnvironment("Production"));

        var result = validator.Validate(null, new HostAuthenticationOptions
        {
            CookieName = "__Host-ICP.Platform.Auth",
            KeyRingPath = Path.Combine(AppContext.BaseDirectory, "keys"),
            ProtectKeysAtRest = false
        });

        Assert.True(result.Failed);
        Assert.Contains("AUTH_KEY_RING_INVALID", result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionKeyRing_IsOutsideInstallAndEncryptedAtRestWithStableApplicationName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"newp-auth-production-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var environment = new TestEnvironment("Production");
            services.AddSingleton<IWebHostEnvironment>(environment);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Host:CookieName"] = "__Host-ICP.Platform.Auth",
                ["Authentication:Host:KeyRingPath"] = root,
                ["Authentication:Host:ApplicationName"] = "ICP.Platform.HostApi.Authentication.v1",
                ["Authentication:Host:ProtectKeysAtRest"] = "true"
            }).Build();
            services.AddHostAuthentication(configuration, environment);
            using var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetRequiredService<IOptions<HostAuthenticationOptions>>().Value);
            var startup = provider.GetServices<IHostedService>().OfType<AuthenticationKeyRingStartupValidator>().Single();

            await startup.StartAsync(CancellationToken.None);

            var keyFile = Assert.Single(Directory.GetFiles(root, "key-*.xml"));
            var keyXml = await File.ReadAllTextAsync(keyFile);
            Assert.Contains("encryptedSecret", keyXml, StringComparison.Ordinal);
            Assert.DoesNotContain("key-ring-ready", keyXml, StringComparison.Ordinal);
            Assert.False(Path.GetFullPath(root).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SessionStoreOutage_Returns503WithoutClearingCookie()
    {
        var cookie = new RecordingCookie();
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication(HostSessionAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, HostSessionAuthenticationHandler>(
                        HostSessionAuthenticationHandler.SchemeName,
                        static _ => { });
                services.AddSingleton<IHostAuthenticationCookie>(cookie);
                services.AddScoped<IAuthenticationSessionRegistry, StoreUnavailableRegistry>();
                services.AddScoped<IIdentityAccountDirectory, UnusedAccountDirectory>();
                services.AddScoped<IAuthorizationService, UnusedAuthorizationService>();
            })
            .Configure(app =>
            {
                app.Run(async context =>
                {
                    var result = await context.AuthenticateAsync(HostSessionAuthenticationHandler.SchemeName);
                    if (!result.Succeeded)
                    {
                        await context.ChallengeAsync(HostSessionAuthenticationHandler.SchemeName);
                    }
                });
            });
        using var server = new TestServer(builder);

        var response = await server.CreateClient().GetAsync("/");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (int)response.StatusCode);
        Assert.Contains(AuthenticationSessionFailureCodes.StoreUnavailable, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(cookie.WasDeleted);
    }

    [Theory]
    [InlineData(false, 401, "AUTH_AUTHENTICATION_REQUIRED")]
    [InlineData(true, 403, "AUTH_FORBIDDEN")]
    public async Task SessionChallengeAndForbidden_ReturnProblemContractWithoutSecrets(
        bool forbidden,
        int expectedStatus,
        string expectedCode)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAuthentication(HostSessionAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, HostSessionAuthenticationHandler>(
                        HostSessionAuthenticationHandler.SchemeName,
                        static _ => { });
                services.AddSingleton<IHostAuthenticationCookie>(new RecordingCookie(readValue: null));
                services.AddScoped<IAuthenticationSessionRegistry, StoreUnavailableRegistry>();
                services.AddScoped<IIdentityAccountDirectory, UnusedAccountDirectory>();
                services.AddScoped<IAuthorizationService, UnusedAuthorizationService>();
            })
            .Configure(app => app.Run(context => forbidden
                ? context.ForbidAsync(HostSessionAuthenticationHandler.SchemeName)
                : context.ChallengeAsync(HostSessionAuthenticationHandler.SchemeName)));
        using var server = new TestServer(builder);

        var response = await server.CreateClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"\"code\":\"{expectedCode}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\":", body, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordVerificationGate_RejectsTwentyFirstConcurrentVerificationWithoutQueueing()
    {
        using var gate = new PasswordVerificationGate(Options.Create(new HostAuthenticationOptions()));
        var leases = new List<IDisposable>();
        for (var index = 0; index < 20; index++)
        {
            leases.Add(Assert.IsAssignableFrom<IDisposable>(await gate.TryEnterAsync(CancellationToken.None)));
        }

        Assert.Null(await gate.TryEnterAsync(CancellationToken.None));

        leases.ForEach(static lease => lease.Dispose());
    }

    [Fact]
    public async Task PasswordVerificationGate_UsesConfiguredNonDefaultConcurrency()
    {
        using var gate = new PasswordVerificationGate(Options.Create(new HostAuthenticationOptions
        {
            MaximumConcurrentPasswordVerifications = 2
        }));
        using var first = Assert.IsAssignableFrom<IDisposable>(await gate.TryEnterAsync(CancellationToken.None));
        using var second = Assert.IsAssignableFrom<IDisposable>(await gate.TryEnterAsync(CancellationToken.None));

        Assert.Null(await gate.TryEnterAsync(CancellationToken.None));
    }

    [Fact]
    public void AuthenticationOptions_InvalidCapacityUsesAccurateStableCode()
    {
        var validator = new HostAuthenticationOptionsValidator(new TestEnvironment("Development"));

        var result = validator.Validate(null, new HostAuthenticationOptions
        {
            LoginAttemptsPerMinute = 0
        });

        Assert.True(result.Failed);
        Assert.Contains(HostAuthenticationOptionsValidator.OptionsInvalid, result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(HostAuthenticationOptionsValidator.KeyRingInvalid, result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ForwardedHeaders_TrustOnlyExplicitProxyAndNetworkConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Host:TrustedProxies:0"] = "10.20.30.40",
            ["Authentication:Host:TrustedNetworks:0"] = "10.30.0.0/16"
        }).Build();

        services.AddHostAuthentication(configuration, new TestEnvironment("Development"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal([System.Net.IPAddress.Parse("10.20.30.40")], options.KnownProxies);
        Assert.Equal([System.Net.IPNetwork.Parse("10.30.0.0/16")], options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void ForwardedHeaders_WithNoTrustedForwarderDoesNotConsumeClientHeaders()
    {
        var services = new ServiceCollection();
        services.AddHostAuthentication(new ConfigurationBuilder().Build(), new TestEnvironment("Development"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public async Task LoginFixedWindow_Returns429AndRetryAfterOnEleventhRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"newp-auth-rate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development"
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Host:KeyRingPath"] = root,
                ["Authentication:Host:CookieName"] = "ICP.Platform.Auth",
                ["Authentication:Host:AllowInsecureDevelopmentCookie"] = "true"
            });
            builder.Services.AddHostAuthentication(builder.Configuration, builder.Environment);
            builder.Services.AddScoped<IAuthenticationSessionPurgeService, NoOpPurge>();
            builder.Services.AddScoped<IAuthenticationSessionRegistry, StoreUnavailableRegistry>();
            builder.Services.AddScoped<IIdentityAccountDirectory, UnusedAccountDirectory>();
            builder.Services.AddScoped<IAuthorizationService, UnusedAuthorizationService>();
            builder.Services.RemoveAll<AuthorizationCatalogInitializer>();
            await using var app = builder.Build();
            app.UseRateLimiter();
            app.MapPost("/api/auth/login", () => Results.NoContent())
                .RequireRateLimiting("authentication-login");
            await app.StartAsync();
            var client = app.GetTestClient();

            for (var request = 0; request < 10; request++)
            {
                Assert.Equal(StatusCodes.Status204NoContent, (int)(await client.PostAsync("/api/auth/login", null)).StatusCode);
            }

            var rejected = await client.PostAsync("/api/auth/login", null);
            Assert.Equal(StatusCodes.Status429TooManyRequests, (int)rejected.StatusCode);
            Assert.Equal("60", Assert.Single(rejected.Headers.GetValues("Retry-After")));
            Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("AUTH_RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("password", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoginFixedWindow_UsesConfiguredNonDefaultLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"newp-auth-rate-custom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Host:KeyRingPath"] = root,
                ["Authentication:Host:LoginAttemptsPerMinute"] = "2"
            });
            builder.Services.AddHostAuthentication(builder.Configuration, builder.Environment);
            builder.Services.AddScoped<IAuthenticationSessionPurgeService, NoOpPurge>();
            builder.Services.AddScoped<IAuthenticationSessionRegistry, StoreUnavailableRegistry>();
            builder.Services.AddScoped<IIdentityAccountDirectory, UnusedAccountDirectory>();
            builder.Services.AddScoped<IAuthorizationService, UnusedAuthorizationService>();
            builder.Services.RemoveAll<AuthorizationCatalogInitializer>();
            await using var app = builder.Build();
            app.UseRateLimiter();
            app.MapPost("/api/auth/login", () => Results.NoContent()).RequireRateLimiting("authentication-login");
            await app.StartAsync();
            var client = app.GetTestClient();

            Assert.Equal(StatusCodes.Status204NoContent, (int)(await client.PostAsync("/api/auth/login", null)).StatusCode);
            Assert.Equal(StatusCodes.Status204NoContent, (int)(await client.PostAsync("/api/auth/login", null)).StatusCode);
            Assert.Equal(StatusCodes.Status429TooManyRequests, (int)(await client.PostAsync("/api/auth/login", null)).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PersistedKeyRing_SurvivesProviderRestartAndWrongRingCannotReadCookie()
    {
        var root = Path.Combine(Path.GetTempPath(), $"newp-auth-{Guid.NewGuid():N}");
        var wrongRoot = root + "-wrong";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(wrongRoot);
        try
        {
            var options = Options.Create(new HostAuthenticationOptions
            {
                CookieName = "__Host-ICP.Platform.Auth"
            });
            var environment = new TestEnvironment("Production");
            var first = Cookie(DataProtectionProvider.Create(new DirectoryInfo(root), Configure), options, environment);
            var context = new DefaultHttpContext();
            first.Append(context, "session-secret", DateTime.UtcNow.AddHours(8));
            var setCookie = Assert.Single(context.Response.Headers.SetCookie).ToString();
            Assert.Contains("__Host-ICP.Platform.Auth=", setCookie, StringComparison.Ordinal);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
            var protectedValue = setCookie.Split(';')[0].Split('=', 2)[1];

            var restarted = Cookie(DataProtectionProvider.Create(new DirectoryInfo(root), Configure), options, environment);
            Assert.True(restarted.TryUnprotect(protectedValue, out var restored));
            Assert.Equal("session-secret", restored);

            var wrong = Cookie(DataProtectionProvider.Create(new DirectoryInfo(wrongRoot), Configure), options, environment);
            Assert.False(wrong.TryUnprotect(protectedValue, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(wrongRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RestrictedSession_OnlyAllowsServerSidePasswordChangeWhitelist()
    {
        var nextCalled = false;
        var middleware = new RestrictedAuthenticationSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/organization/persons";
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Restricted.ToString())
        ], HostSessionAuthenticationHandler.SchemeName));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task RestrictedSession_AllowsAntiforgeryTokenRequiredByPasswordChange()
    {
        var nextCalled = false;
        var middleware = new RestrictedAuthenticationSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/antiforgery-token";
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Restricted.ToString())
        ], HostSessionAuthenticationHandler.SchemeName));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/plugins/active-module-runtime.json")]
    [InlineData("/plugins/localization-manifest.json")]
    [InlineData("/plugins/metadata-runtime-manifest.json")]
    [InlineData("/plugins/runtime/Identity/metadata/navigation/Identity.nav.json")]
    public async Task RestrictedSession_RejectsBusinessModuleRuntimeAssets(string path)
    {
        var nextCalled = false;
        var middleware = new RestrictedAuthenticationSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Restricted.ToString())
        ], HostSessionAuthenticationHandler.SchemeName));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    private static HostAuthenticationCookie Cookie(
        IDataProtectionProvider provider,
        IOptions<HostAuthenticationOptions> options,
        IWebHostEnvironment environment) => new(provider, options, environment);

    private static void Configure(IDataProtectionBuilder builder) =>
        builder.SetApplicationName("ICP.Platform.HostApi.Authentication.v1");

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HostApi.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingCookie(string? readValue = "protected-cookie") : IHostAuthenticationCookie
    {
        public bool WasDeleted { get; private set; }

        public string? Read(HttpContext context) => readValue;

        public bool TryUnprotect(string protectedValue, out string credential)
        {
            credential = "credential";
            return true;
        }

        public void Append(HttpContext context, string credential, DateTime expiresAtUtc) =>
            throw new NotSupportedException();

        public void Delete(HttpContext context) => WasDeleted = true;
    }

    private sealed class StoreUnavailableRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthenticationSessionValidation
            {
                Status = AuthenticationSessionValidationStatus.StoreUnavailable,
                FailureCode = AuthenticationSessionFailureCodes.StoreUnavailable
            });

        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAccountDirectory : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(IdentityAccountSearch search, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAuthorizationService : IAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpPurge : IAuthenticationSessionPurgeService
    {
        public ValueTask<int> PurgeAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
    }
}
