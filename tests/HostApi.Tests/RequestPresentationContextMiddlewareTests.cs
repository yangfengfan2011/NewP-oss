using System.Security.Claims;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Presentation;
using ICP.Platform.Presentation;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class RequestPresentationContextMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenLocaleHeaderIsMissing_UsesManifestDefaultAndTrustedSessionTimeZone()
    {
        var manifest = ActiveLocalizationManifest.Parse(
            """
            {
              "defaultLanguage": "zhs",
              "supportedLanguages": [
                { "code": "zhs", "displayName": "简体中文", "locale": "zh-CN" },
                { "code": "en", "displayName": "English", "locale": "en-US" }
              ],
              "catalogs": {}
            }
            """);
        var context = AuthenticatedContext("Asia/Taipei");
        RequestPresentationContext? captured = null;
        var middleware = new RequestPresentationContextMiddleware(
            _ =>
            {
                captured = Assert.IsType<RequestPresentationContext>(
                    context.Items[HttpRequestPresentationContextAccessor.ItemKey]);
                return Task.CompletedTask;
            });

        await middleware.InvokeAsync(context, manifest);

        Assert.Equal(new RequestPresentationContext("zh-CN", "Asia/Taipei"), captured);
    }

    [Fact]
    public async Task InvokeAsync_WhenLocaleIsUnsupported_ReturnsStableProblemBeforeEndpoint()
    {
        var manifest = Manifest();
        var context = AuthenticatedContext("Asia/Taipei");
        context.Request.Headers[RequestPresentationContextMiddleware.LocaleHeaderName] = "fr-FR";
        var endpointCalled = false;
        var middleware = new RequestPresentationContextMiddleware(_ =>
        {
            endpointCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, manifest);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(endpointCalled);
        context.Response.Body.Position = 0;
        using var document = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            RequestPresentationContextMiddleware.InvalidLocaleCode,
            document.RootElement.GetProperty("code").GetString());
    }

    private static ActiveLocalizationManifest Manifest() => ActiveLocalizationManifest.Parse(
        """
        {
          "defaultLanguage": "zhs",
          "supportedLanguages": [
            { "code": "zhs", "displayName": "简体中文", "locale": "zh-CN" },
            { "code": "en", "displayName": "English", "locale": "en-US" }
          ],
          "catalogs": {}
        }
        """);

    private static DefaultHttpContext AuthenticatedContext(string timeZoneId)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "account-1"),
            new Claim(HostSessionAuthenticationHandler.TimeZoneIdClaim, timeZoneId)
        ], HostSessionAuthenticationHandler.SchemeName));
        return context;
    }
}
