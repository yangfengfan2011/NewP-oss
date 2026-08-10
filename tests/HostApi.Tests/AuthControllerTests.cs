using System.Security.Claims;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class AuthControllerTests
{
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

        var result = await controller.Login(new LoginRequest("admin", "secret"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<AuthUserResponse>(response.Value);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsLocalAdministrator);
        Assert.Equal(1, user.Revision);
        Assert.Equal(AuthenticationSessionType.Restricted, sessions.Registration?.Type);
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
            new("display_name", "Administrator"),
            new(HostSessionAuthenticationHandler.SessionTypeClaim, AuthenticationSessionType.Restricted.ToString()),
            new(HostSessionAuthenticationHandler.AccountRevisionClaim, "7"),
            new("is_local_administrator", "true")
        ], HostSessionAuthenticationHandler.SchemeName));

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<AuthUserResponse>(response.Value);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsLocalAdministrator);
        Assert.Equal(7, user.Revision);
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
        RecordingCookie cookie)
    {
        var controller = new AuthController(
            authenticator,
            sessions,
            new InlineTransactionExecutor(),
            cookie,
            new PasswordVerificationGate(Options.Create(new HostAuthenticationOptions())),
            new StubAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static IdentityAccountSnapshot Account() => new()
    {
        UserEid = "user-1",
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

        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default)
        {
            Registration = registration;
            return ValueTask.FromResult(new AuthenticationSessionIssuance
            {
                Credential = "issued-credential",
                Session = new AuthenticationSessionSnapshot
                {
                    SessionEid = "session-1",
                    UserEid = registration.UserEid,
                    Type = registration.Type,
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

        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
    }
}
