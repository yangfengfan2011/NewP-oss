using ICP.Platform.Authentication.Contracts;

namespace ICP.Platform.HostApi.Authentication;

public sealed class RestrictedAuthenticationSessionMiddleware
{
    private static readonly PathString[] AllowedPaths =
    [
        // password-change 是 Cookie 写请求；受限会话必须能先取得它所需的防伪令牌。
        new("/api/auth/antiforgery-token"),
        new("/api/auth/current-user"),
        new("/api/auth/logout"),
        new("/api/identity/me/password-change")
    ];

    private static readonly PathString[] ShellRuntimeManifestPaths =
    [
        new("/plugins/active-module-runtime.json"),
        new("/plugins/frontend-plugin-manifest.json"),
        new("/plugins/localization-manifest.json"),
        new("/plugins/metadata-runtime-manifest.json")
    ];

    private static readonly PathString ShellRuntimeAssetsPrefix = new("/plugins/runtime");

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
        var allowedShellRuntimeRead = HttpMethods.IsGet(context.Request.Method)
            && (ShellRuntimeManifestPaths.Any(path => context.Request.Path.Equals(path))
                || context.Request.Path.StartsWithSegments(ShellRuntimeAssetsPrefix));
        if (restricted
            && !AllowedPaths.Any(path => context.Request.Path.Equals(path))
            && !allowedShellRuntimeRead)
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
