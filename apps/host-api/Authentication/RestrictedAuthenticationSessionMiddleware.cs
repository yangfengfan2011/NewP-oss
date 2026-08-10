using ICP.Platform.Authentication.Contracts;

namespace ICP.Platform.HostApi.Authentication;

public sealed class RestrictedAuthenticationSessionMiddleware
{
    private static readonly PathString[] AllowedPaths =
    [
        new("/api/auth/current-user"),
        new("/api/auth/logout"),
        new("/api/identity/me/password-change")
    ];

    private readonly RequestDelegate _next;

    public RestrictedAuthenticationSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var restricted = string.Equals(
            context.User.FindFirst(HostSessionAuthenticationHandler.SessionTypeClaim)?.Value,
            AuthenticationSessionType.Restricted.ToString(),
            StringComparison.Ordinal);
        if (restricted && !AllowedPaths.Any(path => context.Request.Path.Equals(path)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                status = 403,
                code = "AUTH_PASSWORD_CHANGE_REQUIRED",
                traceId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
