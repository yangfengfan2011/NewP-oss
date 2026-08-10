using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using PlatformAuthorizationService = ICP.Platform.Modules.Authorization.Services.IAuthorizationService;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class HostSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "HostSession";
    public const string SessionEidClaim = "authentication_session_eid";
    public const string SessionTypeClaim = "authentication_session_type";
    private const string StoreUnavailableItem = "HostSession.StoreUnavailable";

    private readonly IHostAuthenticationCookie _cookie;
    private readonly IAuthenticationSessionRegistry _registry;
    private readonly IIdentityAccountDirectory _accountDirectory;
    private readonly PlatformAuthorizationService _authorizationService;

    public HostSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHostAuthenticationCookie cookie,
        IAuthenticationSessionRegistry registry,
        IIdentityAccountDirectory accountDirectory,
        PlatformAuthorizationService authorizationService)
        : base(options, logger, encoder)
    {
        _cookie = cookie;
        _registry = registry;
        _accountDirectory = accountDirectory;
        _authorizationService = authorizationService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var protectedValue = _cookie.Read(Context);
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return AuthenticateResult.NoResult();
        }

        if (!_cookie.TryUnprotect(protectedValue, out var credential))
        {
            _cookie.Delete(Context);
            return AuthenticateResult.Fail("AUTH_SESSION_INVALID");
        }

        var validation = await _registry.ValidateAsync(credential, Context.RequestAborted);
        if (validation.Status == AuthenticationSessionValidationStatus.StoreUnavailable)
        {
            Context.Items[StoreUnavailableItem] = true;
            return AuthenticateResult.Fail(AuthenticationSessionFailureCodes.StoreUnavailable);
        }

        if (validation is not { Status: AuthenticationSessionValidationStatus.Active, Session: not null })
        {
            _cookie.Delete(Context);
            return AuthenticateResult.Fail("AUTH_SESSION_INVALID");
        }

        try
        {
            var account = await _accountDirectory.FindAsync(validation.Session.UserEid, Context.RequestAborted);
            if (account is not { Status: IdentityAccountStatus.Enabled })
            {
                _cookie.Delete(Context);
                return AuthenticateResult.Fail("AUTH_SESSION_INVALID");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, account.UserEid),
                new(ClaimTypes.Name, account.ECODE),
                new("display_name", account.ENAME),
                new(SessionEidClaim, validation.Session.SessionEid),
                new(SessionTypeClaim, validation.Session.Type.ToString()),
                new("is_local_administrator", account.IsLocalAdministrator ? "true" : "false")
            };
            foreach (var role in await _authorizationService.GetUserRolesAsync(account.UserEid, Context.RequestAborted))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (OperationCanceledException) when (Context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            Context.Items[StoreUnavailableItem] = true;
            return AuthenticateResult.Fail(AuthenticationSessionFailureCodes.StoreUnavailable);
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(StoreUnavailableItem))
        {
            Context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Context.Response.ContentType = "application/problem+json";
            return Context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = 503,
                code = AuthenticationSessionFailureCodes.StoreUnavailable,
                traceId = Context.TraceIdentifier
            }));
        }

        Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
