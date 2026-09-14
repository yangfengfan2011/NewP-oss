using System.Security.Claims;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Sdk.Http;
using PlatformAuthorizationService = ICP.Platform.Modules.Authorization.Services.IAuthorizationService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/auth")]
[PlatformRequestValidation]
public sealed class AuthController : ControllerBase
{
    public static readonly EventId LoginFailedEventId = new(4101, "AuthenticationLoginFailed");
    private static readonly Action<ILogger, string, Exception?> LogLoginFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            LoginFailedEventId,
            "Authentication login failed with result category {ResultCategory}.");
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
    private readonly ILogger<AuthController> _logger;
    private readonly IFeatureGrantProjection? _featureGrants;

    public AuthController(
        ILocalCredentialAuthenticator credentialAuthenticator,
        IAuthenticationSessionRegistry sessionRegistry,
        IPlatformTransactionExecutor transactionExecutor,
        IHostAuthenticationCookie cookie,
        PasswordVerificationGate passwordVerificationGate,
        PlatformAuthorizationService authorizationService,
        ILogger<AuthController> logger,
        IFeatureGrantProjection? featureGrants = null)
    {
        _credentialAuthenticator = credentialAuthenticator;
        _sessionRegistry = sessionRegistry;
        _transactionExecutor = transactionExecutor;
        _cookie = cookie;
        _passwordVerificationGate = passwordVerificationGate;
        _authorizationService = authorizationService;
        _logger = logger;
        _featureGrants = featureGrants;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication-login")]
    // 通过 Authentication 注册表冻结登录时区，成功登记后才签发 Cookie。
    public async Task<ActionResult<AuthUserResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        using var verificationLease = await _passwordVerificationGate.TryEnterAsync(cancellationToken);
        if (verificationLease is null)
        {
            LogLoginFailure(_logger, "PasswordVerificationBusy", null);
            Response.Headers.RetryAfter = "1";
            return ProblemResult(429, "AUTH_PASSWORD_VERIFICATION_BUSY");
        }

        var authentication = await _credentialAuthenticator.AuthenticateAsync(
            request.ECODE,
            request.Password,
            cancellationToken);
        if (authentication.Status == LocalCredentialAuthenticationStatus.StoreUnavailable)
        {
            LogLoginFailure(_logger, "StoreUnavailable", null);
            return ProblemResult(503, authentication.FailureCode ?? LocalCredentialAuthenticationFailureCodes.StoreUnavailable);
        }

        if (authentication is not { Status: LocalCredentialAuthenticationStatus.Succeeded, Account: not null })
        {
            LogLoginFailure(_logger, "InvalidCredentials", null);
            return ProblemResult(401, "AUTH_INVALID_CREDENTIALS");
        }

        var permissions = await _authorizationService.GetUserPermissionsAsync(
            authentication.Account.AccountEid,
            cancellationToken);
        IReadOnlyList<string> features;
        try { features = _featureGrants is null ? [] : await _featureGrants.GetCurrentPositionsAsync(authentication.Account.AccountEid, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException) { return ProblemResult(503, "AUTHORIZATION_DECISION_UNAVAILABLE"); }
        AuthenticationSessionIssuance issuance;
        try
        {
            issuance = await _transactionExecutor.ExecuteAsync(
                token => _sessionRegistry.RegisterAsync(new AuthenticationSessionRegistration
                {
                    AccountEid = authentication.Account.AccountEid,
                    Type = authentication.IsTemporary
                        ? AuthenticationSessionType.Restricted
                        : AuthenticationSessionType.Regular,
                    ActorId = authentication.Account.AccountEid,
                    TimeZoneId = request.TimeZoneId
                }, token),
                cancellationToken);
        }
        catch (AuthenticationSessionException exception)
            when (exception.FailureCode == AuthenticationSessionFailureCodes.InvalidTimeZone)
        {
            LogLoginFailure(_logger, "InvalidTimeZone", null);
            return ProblemResult(StatusCodes.Status400BadRequest, exception.FailureCode);
        }
        _cookie.Append(HttpContext, issuance.Credential, issuance.Session.ExpiresAtUtc);

        return Ok(ToResponse(
            authentication.Account,
            permissions,
            authentication.IsTemporary,
            issuance.Session.TimeZoneId) with { FeaturePositions = features });
    }

    [HttpPost("logout")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var sessionEid = User.FindFirstValue(HostSessionAuthenticationHandler.SessionEidClaim);
        var accountEid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(sessionEid) && !string.IsNullOrWhiteSpace(accountEid))
        {
            await _transactionExecutor.ExecuteAsync(
                token => _sessionRegistry.RevokeCurrentAsync(new AuthenticationSessionRevocation
                {
                    SessionEid = sessionEid,
                    Reason = "UserLogout",
                    ActorId = accountEid
                }, token),
                cancellationToken);
        }

        _cookie.Delete(HttpContext);
        return NoContent();
    }

    [HttpGet("current-user")]
    [AuthenticatedOnly]
    // 从当前已验证 principal 投影同一会话时区，不接受请求覆盖。
    public async Task<ActionResult<AuthUserResponse>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var accountCode = User.Identity?.Name;
        var ename = User.FindFirstValue("ename");
        var timeZoneId = User.FindFirstValue(HostSessionAuthenticationHandler.TimeZoneIdClaim);
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(accountCode)
            || string.IsNullOrWhiteSpace(ename)
            || string.IsNullOrWhiteSpace(timeZoneId))
        {
            _cookie.Delete(HttpContext);
            return ProblemResult(401, "AUTH_SESSION_INVALID");
        }

        var permissions = await _authorizationService.GetUserPermissionsAsync(userId, cancellationToken);
        IReadOnlyList<string> features;
        try { features = _featureGrants is null ? [] : await _featureGrants.GetCurrentPositionsAsync(userId, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException) { return ProblemResult(503, "AUTHORIZATION_DECISION_UNAVAILABLE"); }
        var restricted = string.Equals(
            User.FindFirstValue(HostSessionAuthenticationHandler.SessionTypeClaim),
            AuthenticationSessionType.Restricted.ToString(),
            StringComparison.Ordinal);
        if (!long.TryParse(
                User.FindFirstValue(HostSessionAuthenticationHandler.AccountRevisionClaim),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var revision))
        {
            _cookie.Delete(HttpContext);
            return ProblemResult(401, "AUTH_SESSION_INVALID");
        }

        var isLocalAdministrator = string.Equals(
            User.FindFirstValue("is_local_administrator"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        return Ok(new AuthUserResponse(
            userId,
            accountCode,
            ename,
            permissions,
            restricted,
            revision,
            isLocalAdministrator,
            timeZoneId) { FeaturePositions = features });
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

    // 组合登录成功响应，并保持响应时区与本次签发的会话快照一致。
    private static AuthUserResponse ToResponse(
        IdentityAccountSnapshot account,
        IReadOnlyList<string> permissions,
        bool mustChangePassword,
        string timeZoneId) =>
        new(
            account.AccountEid,
            account.ECODE,
            account.ENAME,
            permissions,
            mustChangePassword,
            account.Revision,
            account.IsLocalAdministrator,
            timeZoneId);
}

public sealed record LoginRequest(string ECODE, string Password, string? TimeZoneId = null);

public sealed record AntiforgeryTokenResponse(string RequestToken);

public sealed record AuthUserResponse(
    string Id,
    string ECODE,
    string ENAME,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    long Revision,
    bool IsLocalAdministrator,
    string TimeZoneId)
{
    public IReadOnlyList<string> FeaturePositions { get; init; } = [];
}
