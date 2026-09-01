using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Presentation;

namespace ICP.Platform.HostApi.Presentation;

/// <summary>
/// 用受控请求 locale 与已验证会话时区建立请求级展示上下文。
/// </summary>
public sealed class RequestPresentationContextMiddleware(RequestDelegate next)
{
    /// <summary>Shell 发送规范 locale 的受控请求头。</summary>
    public const string LocaleHeaderName = "X-ICP-Locale";

    /// <summary>请求 locale 不属于活动 manifest 时返回的稳定错误码。</summary>
    public const string InvalidLocaleCode = "REQUEST_LOCALE_INVALID";

    /// <summary>校验 locale 与可信会话时区，并在调用后续中间件前投影展示上下文。</summary>
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
