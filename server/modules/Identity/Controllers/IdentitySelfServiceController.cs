using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Identity.Controllers;

[ApiController]
[Route("api/identity/me")]
[AuthenticatedOnly]
[PlatformRequestValidation]
public sealed class IdentitySelfServiceController : ControllerBase
{
    private readonly IIdentityAccountApplicationService _accounts;

    public IdentitySelfServiceController(IIdentityAccountApplicationService accounts)
    {
        _accounts = accounts;
    }

    [HttpPost("password-change")]
    public async Task<ActionResult<IdentityPasswordChangeResponse>> ChangePassword(
        [FromBody] ChangeIdentityPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var accountEid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(accountEid))
        {
            return ProblemResult(StatusCodes.Status401Unauthorized, "AUTH_SESSION_INVALID");
        }

        try
        {
            var result = await _accounts.ChangePasswordAsync(
                new ChangeIdentityPasswordCommand(
                    accountEid,
                    request.CurrentPassword,
                    request.NewPassword,
                    request.ExpectedRevision,
                    new IdentityActorContext(
                        accountEid,
                        IdentitySecurityActorKind.IdentityAccount,
                        HttpContext.TraceIdentifier)),
                cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Ok(new IdentityPasswordChangeResponse(
                IdentityAccountResponse.From(result.Account),
                result.Session.Credential,
                result.Session.Session.ExpiresAtUtc));
        }
        catch (IdentityApplicationException exception)
        {
            var status = exception.FailureCode switch
            {
                IdentityFailureCodes.AccountNotFound => StatusCodes.Status404NotFound,
                IdentityFailureCodes.RevisionConflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return IdentityControllerProblemDetails.FromApplicationException(this, status, exception);
        }
    }

    private ObjectResult ProblemResult(int status, string code)
        => IdentityControllerProblemDetails.Create(this, status, code);
}

public sealed record ChangeIdentityPasswordRequest(
    string CurrentPassword,
    string NewPassword,
    [Range(0, long.MaxValue)] long ExpectedRevision);

public sealed record IdentityPasswordChangeResponse(
    IdentityAccountResponse Account,
    [property: JsonIgnore] string SessionCredential,
    [property: JsonIgnore] DateTime SessionExpiresAtUtc) : IAuthenticationSessionCookieResponse
{
    public string GetSessionCredential() => SessionCredential;

    public DateTime GetSessionExpiresAtUtc() => SessionExpiresAtUtc;
}
