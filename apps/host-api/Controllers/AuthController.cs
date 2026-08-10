using System.Security.Claims;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Persistence.Abstractions;
using PlatformAuthorizationService = ICP.Platform.Modules.Authorization.Services.IAuthorizationService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("antiforgery-token")]
    [AllowAnonymous]
    public ActionResult<AntiforgeryTokenResponse> GetAntiforgeryToken(
        [FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }

    private readonly ILocalCredentialAuthenticator _credentialAuthenticator;
    private readonly IAuthenticationSessionRegistry _sessionRegistry;
    private readonly IPlatformTransactionExecutor _transactionExecutor;
    private readonly IHostAuthenticationCookie _cookie;
    private readonly PasswordVerificationGate _passwordVerificationGate;
    private readonly PlatformAuthorizationService _authorizationService;

    public AuthController(
        ILocalCredentialAuthenticator credentialAuthenticator,
        IAuthenticationSessionRegistry sessionRegistry,
        IPlatformTransactionExecutor transactionExecutor,
        IHostAuthenticationCookie cookie,
        PasswordVerificationGate passwordVerificationGate,
        PlatformAuthorizationService authorizationService)
    {
        _credentialAuthenticator = credentialAuthenticator;
        _sessionRegistry = sessionRegistry;
        _transactionExecutor = transactionExecutor;
        _cookie = cookie;
        _passwordVerificationGate = passwordVerificationGate;
        _authorizationService = authorizationService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication-login")]
    public async Task<ActionResult<AuthUserResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        using var verificationLease = await _passwordVerificationGate.TryEnterAsync(cancellationToken);
        if (verificationLease is null)
        {
            Response.Headers.RetryAfter = "1";
            return ProblemResult(429, "AUTH_PASSWORD_VERIFICATION_BUSY");
        }

        var authentication = await _credentialAuthenticator.AuthenticateAsync(
            request.Username,
            request.Password,
            cancellationToken);
        if (authentication.Status == LocalCredentialAuthenticationStatus.StoreUnavailable)
        {
            return ProblemResult(503, authentication.FailureCode ?? LocalCredentialAuthenticationFailureCodes.StoreUnavailable);
        }

        if (authentication is not { Status: LocalCredentialAuthenticationStatus.Succeeded, Account: not null })
        {
            return ProblemResult(401, "AUTH_INVALID_CREDENTIALS");
        }

        var permissions = await _authorizationService.GetUserPermissionsAsync(
            authentication.Account.UserEid,
            cancellationToken);
        var issuance = await _transactionExecutor.ExecuteAsync(
            token => _sessionRegistry.RegisterAsync(new AuthenticationSessionRegistration
            {
                UserEid = authentication.Account.UserEid,
                Type = authentication.IsTemporary
                    ? AuthenticationSessionType.Restricted
                    : AuthenticationSessionType.Regular,
                ActorId = authentication.Account.UserEid
            }, token),
            cancellationToken);
        _cookie.Append(HttpContext, issuance.Credential, issuance.Session.ExpiresAtUtc);

        return Ok(ToResponse(authentication.Account, permissions, authentication.IsTemporary));
    }

    [HttpPost("logout")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var sessionEid = User.FindFirstValue(HostSessionAuthenticationHandler.SessionEidClaim);
        var userEid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(sessionEid) && !string.IsNullOrWhiteSpace(userEid))
        {
            await _transactionExecutor.ExecuteAsync(
                token => _sessionRegistry.RevokeCurrentAsync(new AuthenticationSessionRevocation
                {
                    SessionEid = sessionEid,
                    Reason = "UserLogout",
                    ActorId = userEid
                }, token),
                cancellationToken);
        }

        _cookie.Delete(HttpContext);
        return NoContent();
    }

    [HttpGet("current-user")]
    [AuthenticatedOnly]
    public async Task<ActionResult<AuthUserResponse>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var accountCode = User.Identity?.Name;
        var displayName = User.FindFirstValue("display_name");
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(accountCode)
            || string.IsNullOrWhiteSpace(displayName))
        {
            _cookie.Delete(HttpContext);
            return ProblemResult(401, "AUTH_SESSION_INVALID");
        }

        var permissions = await _authorizationService.GetUserPermissionsAsync(userId, cancellationToken);
        var restricted = string.Equals(
            User.FindFirstValue(HostSessionAuthenticationHandler.SessionTypeClaim),
            AuthenticationSessionType.Restricted.ToString(),
            StringComparison.Ordinal);
        return Ok(new AuthUserResponse(userId, accountCode, displayName, permissions, restricted));
    }

    private ObjectResult ProblemResult(int status, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = code
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = StatusCode(status, problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static AuthUserResponse ToResponse(
        IdentityAccountSnapshot account,
        IReadOnlyList<string> permissions,
        bool mustChangePassword) =>
        new(account.UserEid, account.ECODE, account.ENAME, permissions, mustChangePassword);
}

public sealed record LoginRequest(string Username, string Password);

public sealed record AntiforgeryTokenResponse(string RequestToken);

public sealed record AuthUserResponse(
    string Id,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);
