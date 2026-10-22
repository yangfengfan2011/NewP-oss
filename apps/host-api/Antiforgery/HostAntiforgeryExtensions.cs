using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Json;
using ICP.Platform.HostApi.Authentication;

namespace ICP.Platform.HostApi.Antiforgery;

public static class HostAntiforgeryExtensions
{
    private static readonly HashSet<string> ProtectedMethods =
    [
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    ];

    public static IServiceCollection AddHostAntiforgery(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HostAntiforgeryDefaults.HeaderName;
            options.Cookie.Name = environment.IsProduction()
                ? "__Host-ICP.Platform.Antiforgery"
                : "ICP.Platform.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });
        return services;
    }

    public static IApplicationBuilder UseHostAntiforgery(this IApplicationBuilder application) =>
        application.UseMiddleware<HostAntiforgeryMiddleware>();

    internal static bool RequiresValidation(HttpContext context, IHostAuthenticationCookie authenticationCookie)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || !ProtectedMethods.Contains(request.Method))
        {
            return false;
        }

        // 防伪保护的是浏览器自动附带的认证 Cookie；显式 header 等非 Cookie 认证不受 CSRF 威胁。
        // 登录会建立 Cookie，登录/退出端点即使当前没有认证 Cookie 也必须保持同一写请求契约。
        return request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase)
            || authenticationCookie.Read(context) is not null;
    }
}

public sealed class HostAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IHostAuthenticationCookie authenticationCookie)
    {
        if (!HostAntiforgeryExtensions.RequiresValidation(context, authenticationCookie))
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            // 防伪失败必须在任何业务 endpoint 前收敛为稳定机器码，客户端才可安全地仅重放这一类请求。
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new
                {
                    status = StatusCodes.Status400BadRequest,
                    title = HostAntiforgeryFailureCodes.InvalidToken,
                    code = HostAntiforgeryFailureCodes.InvalidToken,
                    traceId = context.TraceIdentifier
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }
}
