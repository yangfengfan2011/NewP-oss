using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Presentation;

namespace ICP.Platform.HostApi.Presentation;

public sealed class RequestPresentationContextMiddleware(RequestDelegate next)
{
    public const string LocaleHeaderName = "X-ICP-Locale";
    public const string InvalidLocaleCode = "REQUEST_LOCALE_INVALID";

    public async Task InvokeAsync(HttpContext context, ActiveLocalizationManifest manifest)
    {
        var requestedLocale = context.Request.Headers[LocaleHeaderName].ToString();
        if (!manifest.TryResolveLocale(requestedLocale, out var locale))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                status = StatusCodes.Status400BadRequest,
                code = InvalidLocaleCode,
                traceId = context.TraceIdentifier
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var timeZoneId = context.User.FindFirst(HostSessionAuthenticationHandler.TimeZoneIdClaim)?.Value;
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = StatusCodes.Status401Unauthorized,
                    code = "AUTH_SESSION_INVALID",
                    traceId = context.TraceIdentifier
                }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
                return;
            }

            context.Items[HttpRequestPresentationContextAccessor.ItemKey] =
                new RequestPresentationContext(locale, timeZoneId);
        }

        await next(context);
    }
}
