using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class AuthenticationSessionCookieResultFilterTests
{
    [Fact]
    public async Task SuccessfulRotationResult_AppendsProtectedCookieWithoutSerializingCredential()
    {
        var cookie = new RecordingCookie();
        var filter = new AuthenticationSessionCookieResultFilter(cookie);
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var result = new ObjectResult(new RotationResponse(
            "new-session-secret",
            new DateTime(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
        var executing = new ResultExecutingContext(
            actionContext,
            [],
            result,
            controller: new object());

        await filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                [],
                result,
                controller: new object())));

        Assert.Equal("new-session-secret", cookie.Credential);
        Assert.Equal(new DateTime(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc), cookie.ExpiresAtUtc);
    }

    [Fact]
    public async Task ErrorResult_DoesNotAppendCookieOrReplayRotation()
    {
        var cookie = new RecordingCookie();
        var filter = new AuthenticationSessionCookieResultFilter(cookie);
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var result = new ObjectResult(new RotationResponse("stale-secret", DateTime.UtcNow))
        {
            StatusCode = StatusCodes.Status409Conflict
        };
        var executing = new ResultExecutingContext(actionContext, [], result, new object());

        await filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(actionContext, [], result, new object())));

        Assert.Null(cookie.Credential);
    }

    private sealed record RotationResponse(
        string SessionCredential,
        DateTime SessionExpiresAtUtc) : IAuthenticationSessionCookieResponse
    {
        public string GetSessionCredential() => SessionCredential;
        public DateTime GetSessionExpiresAtUtc() => SessionExpiresAtUtc;
    }

    private sealed class RecordingCookie : IHostAuthenticationCookie
    {
        public string? Credential { get; private set; }
        public DateTime? ExpiresAtUtc { get; private set; }
        public string? Read(HttpContext context) => null;
        public bool TryUnprotect(string protectedValue, out string credential) => throw new NotSupportedException();
        public void Append(HttpContext context, string credential, DateTime expiresAtUtc)
        {
            Credential = credential;
            ExpiresAtUtc = expiresAtUtc;
        }
        public void Delete(HttpContext context) => throw new NotSupportedException();
    }
}
