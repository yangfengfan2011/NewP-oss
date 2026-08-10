using System.Net;
using System.Threading.RateLimiting;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Extensions;

public static class HostAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddHostAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<HostAuthenticationOptions>()
            .Bind(configuration.GetSection(HostAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HostAuthenticationOptions>, HostAuthenticationOptionsValidator>();

        var options = configuration.GetSection(HostAuthenticationOptions.SectionName)
            .Get<HostAuthenticationOptions>() ?? new HostAuthenticationOptions();
        var validation = new HostAuthenticationOptionsValidator(environment).Validate(null, options);
        if (validation.Failed)
        {
            throw new InvalidOperationException(validation.FailureMessage);
        }

        var keyRingPath = options.KeyRingPath;
        if (string.IsNullOrWhiteSpace(keyRingPath) && environment.IsDevelopment())
        {
            keyRingPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ICP.Platform",
                "Authentication",
                "keys");
        }

        var dataProtection = services.AddDataProtection().SetApplicationName(options.ApplicationName);
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        if (options.ProtectKeysAtRest && OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddAuthentication(HostSessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HostSessionAuthenticationHandler>(
                HostSessionAuthenticationHandler.SchemeName,
                static _ => { });
        services.AddSingleton<IHostAuthenticationCookie, HostAuthenticationCookie>();
        services.AddSingleton<PasswordVerificationGate>();
        services.AddHostedService<AuthenticationKeyRingStartupValidator>();
        services.AddHostedService<AuthenticationSessionPurgeHostedService>();
        services.AddScoped<AuthorizationCatalogInitializer>();

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            var hasTrustedForwarder = options.TrustedProxies.Length > 0 || options.TrustedNetworks.Length > 0;
            forwarded.ForwardedHeaders = hasTrustedForwarder
                ? ForwardedHeaders.XForwardedFor
                : ForwardedHeaders.None;
            forwarded.ForwardLimit = 1;
            // 框架默认信任 loopback；本协议要求转发头信任集合完全来自显式配置。
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var address in options.TrustedProxies.Select(IPAddress.Parse))
            {
                forwarded.KnownProxies.Add(address);
            }

            foreach (var network in options.TrustedNetworks)
            {
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.OnRejected = static (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
            rateLimiter.AddPolicy("authentication-login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.LoginAttemptsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }
}
