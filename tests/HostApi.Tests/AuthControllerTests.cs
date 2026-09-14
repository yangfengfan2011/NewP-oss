using System.Security.Claims;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public void AuthenticationHttpContract_UsesOnlyEcodeAndEname()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<LoginRequest>("""{"ecode":"admin","password":"secret","timeZoneId":"Asia/Taipei"}""", options);
        var response = JsonSerializer.Serialize(
            new AuthUserResponse("User_1", "admin", "Administrator", false, 1, true, "Asia/Taipei"),
            options);

        Assert.Equal("admin", request?.ECODE);
        Assert.Equal("Asia/Taipei", request?.TimeZoneId);
        Assert.Contains("\"timeZoneId\":\"Asia/Taipei\"", response, StringComparison.Ordinal);
        Assert.Contains("\"ecode\":", response, StringComparison.Ordinal);
        Assert.Contains("\"ename\":", response, StringComparison.Ordinal);
        Assert.DoesNotContain("username", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("displayName", response, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FeatureProjectionFailure_PreservesUnavailableResponseWithoutIssuingSession(bool login)
    {
        var sessions = new RecordingSessionRegistry();
        var cookie = new RecordingCookie();
        var controller = Controller(new StubAuthenticator { Result = new LocalCredentialAuthenticationResult
            { Status = LocalCredentialAuthenticationStatus.Succeeded, Account = Account() } }, sessions, cookie,
            features: new FailingFeatureProjection());
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "user-1"), new(ClaimTypes.Name, "admin"), new("ename", "Administrator"),
            new(HostSessionAuthenticationHandler.TimeZoneIdClaim, "Asia/Taipei")], HostSessionAuthenticationHandler.SchemeName));
        var result = login ? await controller.Login(new LoginRequest("admin", "secret"), CancellationToken.None)
            : await controller.GetCurrentUser(CancellationToken.None);
        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("AUTHORIZATION_DECISION_UNAVAILABLE", Assert.IsType<ProblemDetails>(response.Value).Extensions["code"]);
        Assert.Null(cookie.AppendedCredential);
        Assert.Null(sessions.Registration);
    }

    [Fact]
    public async Task Login_TemporaryCredentialCreatesRestrictedServerSessionAndProtectedCookie()
    {
        var authenticator = new StubAuthenticator
        {
            Result = new LocalCredentialAuthenticationResult
            {
                Status = LocalCredentialAuthenticationStatus.Succeeded,
                IsTemporary = true,
                Account = Account()
            }
        };
        var sessions = new RecordingSessionRegistry();
        var cookie = new RecordingCookie();
        var controller = Controller(authenticator, sessions, cookie);

        var result = await controller.Login(new LoginRequest("admin", "secret", "Europe/Paris"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<AuthUserResponse>(response.Value);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsLocalAdministrator);
        Assert.Equal(1, user.Revision);
        Assert.Equal("Europe/Paris", user.TimeZoneId);
        Assert.Equal(AuthenticationSessionType.Restricted, sessions.Registration?.Type);
        Assert.Equal("Europe/Paris", sessions.Registration?.TimeZoneId);
        Assert.Equal("issued-credential", cookie.AppendedCredential);
    }

    [Fact]
    public async Task CurrentUser_ProjectsRestrictedSessionRevisionAndLocalAdministratorStatus()
    {
        var controller = Controller(new StubAuthenticator(), new RecordingSessionRegistry(), new RecordingCookie());
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Name, "admin"),
            new("ename", "Administrator"),
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Restricted.ToString()),
            new(HostSessionAuthenticationHandler.AccountRevisionClaim, "7"),
            new(HostSessionAuthenticationHandler.TimeZoneIdClaim, "Asia/Taipei"),
            new("is_local_administrator", "true")
        ], HostSessionAuthenticationHandler.SchemeName));

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<AuthUserResponse>(response.Value);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsLocalAdministrator);
        Assert.Equal(7, user.Revision);
        Assert.Equal("Asia/Taipei", user.TimeZoneId);
    }

    [Fact]
    public async Task CurrentUser_RejectsPrincipalWithoutTrustedSessionTimeZone()
    {
        var cookie = new RecordingCookie();
        var controller = Controller(new StubAuthenticator(), new RecordingSessionRegistry(), cookie);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Name, "admin"),
            new("ename", "Administrator"),
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Regular.ToString()),
            new(HostSessionAuthenticationHandler.AccountRevisionClaim, "7")
        ], HostSessionAuthenticationHandler.SchemeName));

        var result = await controller.GetCurrentUser(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.True(cookie.WasDeleted);
    }

    [Fact]
    public async Task Login_InvalidTimeZoneReturnsStableProblemAndDoesNotIssueCookie()
    {
        var sessions = new RecordingSessionRegistry
        {
            Failure = new AuthenticationSessionException(AuthenticationSessionFailureCodes.InvalidTimeZone)
        };
        var cookie = new RecordingCookie();
        var controller = Controller(new StubAuthenticator
        {
            Result = new LocalCredentialAuthenticationResult
            {
                Status = LocalCredentialAuthenticationStatus.Succeeded,
                Account = Account()
            }
        }, sessions, cookie);

        var result = await controller.Login(
            new LoginRequest("admin", "secret", "China Standard Time"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(AuthenticationSessionFailureCodes.InvalidTimeZone, Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
        Assert.Null(cookie.AppendedCredential);
    }

    [Fact]
    public async Task Login_InvalidCredentialUsesOneIndistinguishable401CodeAndCreatesNoSession()
    {
        var authenticator = new StubAuthenticator
        {
            Result = new LocalCredentialAuthenticationResult
            {
                Status = LocalCredentialAuthenticationStatus.Invalid
            }
        };
        var sessions = new RecordingSessionRegistry();
        var cookie = new RecordingCookie();
        var controller = Controller(authenticator, sessions, cookie);

        var result = await controller.Login(new LoginRequest("missing", "wrong"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Null(sessions.Registration);
        Assert.Null(cookie.AppendedCredential);
    }

    [Fact]
    public async Task Login_InvalidCredentialWritesCategorizedOperationalEventWithoutAccountOrPassword()
    {
        var logger = new RecordingLogger<AuthController>();
        var controller = Controller(
            new StubAuthenticator
            {
                Result = new LocalCredentialAuthenticationResult { Status = LocalCredentialAuthenticationStatus.Invalid }
            },
            new RecordingSessionRegistry(),
            new RecordingCookie(),
            logger);

        await controller.Login(new LoginRequest("sensitive-ecode", "sensitive-password"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(AuthController.LoginFailedEventId, entry.EventId);
        Assert.Contains("InvalidCredentials", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-ecode", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-password", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_RevokesOnlyCurrentSessionAndClearsCookie()
    {
        var sessions = new RecordingSessionRegistry();
        var cookie = new RecordingCookie();
        var controller = Controller(new StubAuthenticator(), sessions, cookie);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(HostSessionAuthenticationHandler.SessionEidClaim, "session-1")
        ], HostSessionAuthenticationHandler.SchemeName));

        var result = await controller.Logout(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("session-1", sessions.Revocation?.SessionEid);
        Assert.True(cookie.WasDeleted);
    }

    private static AuthController Controller(
        ILocalCredentialAuthenticator authenticator,
        RecordingSessionRegistry sessions,
        RecordingCookie cookie,
        ILogger<AuthController>? logger = null, IFeatureGrantProjection? features = null)
    {
        var controller = new AuthController(
            authenticator,
            sessions,
            new InlineTransactionExecutor(),
            cookie,
            new PasswordVerificationGate(Options.Create(new HostAuthenticationOptions())),
            logger ?? NullLogger<AuthController>.Instance, features ?? new FixedFeatureProjection())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, formatter(state, exception)));
    }

    private static IdentityAccountSnapshot Account() => new()
    {
        AccountEid = "user-1",
        ECODE = "admin",
        ENAME = "Administrator",
        Status = IdentityAccountStatus.Enabled,
        Revision = 1,
        IsLocalAdministrator = true
    };

    private sealed class StubAuthenticator : ILocalCredentialAuthenticator
    {
        public LocalCredentialAuthenticationResult Result { get; init; } = new()
        {
            Status = LocalCredentialAuthenticationStatus.Invalid
        };

        public ValueTask<LocalCredentialAuthenticationResult> AuthenticateAsync(string accountCode, string password, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result);
    }

    private sealed class RecordingSessionRegistry : IAuthenticationSessionRegistry
    {
        public AuthenticationSessionRegistration? Registration { get; private set; }
        public AuthenticationSessionRevocation? Revocation { get; private set; }
        public Exception? Failure { get; init; }

        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default)
        {
            Registration = registration;
            if (Failure is not null)
            {
                return ValueTask.FromException<AuthenticationSessionIssuance>(Failure);
            }
            return ValueTask.FromResult(new AuthenticationSessionIssuance
            {
                Credential = "issued-credential",
                Session = new AuthenticationSessionSnapshot
                {
                    SessionEid = "session-1",
                    AccountEid = registration.AccountEid,
                    Type = registration.Type,
                    TimeZoneId = registration.TimeZoneId ?? "Asia/Shanghai",
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(8)
                }
            });
        }

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default)
        {
            Revocation = revocation;
            return ValueTask.FromResult(true);
        }

        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingCookie : IHostAuthenticationCookie
    {
        public string? AppendedCredential { get; private set; }
        public bool WasDeleted { get; private set; }
        public string? Read(HttpContext context) => null;
        public bool TryUnprotect(string protectedValue, out string credential) { credential = string.Empty; return false; }
        public void Append(HttpContext context, string credential, DateTime expiresAtUtc) => AppendedCredential = credential;
        public void Delete(HttpContext context) => WasDeleted = true;
    }

    private sealed class InlineTransactionExecutor : IPlatformTransactionExecutor
    {
        public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default) =>
            registerChanges(cancellationToken);
    }

    private sealed class FixedFeatureProjection : IFeatureGrantProjection
    {
        public Task<IReadOnlyList<string>> GetCurrentPositionsAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["declared.feature"]);
    }
    private sealed class FailingFeatureProjection : IFeatureGrantProjection
    {
        public Task<IReadOnlyList<string>> GetCurrentPositionsAsync(string subjectId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("unavailable feature store");
    }
}
