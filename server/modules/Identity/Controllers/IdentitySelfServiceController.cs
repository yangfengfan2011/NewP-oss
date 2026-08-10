using System.Security.Claims;
using System.Text.Json.Serialization;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Identity.Controllers;

[ApiController]
[Route("api/identity/me")]
[AuthenticatedOnly]
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
        var userEid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userEid))
        {
            return ProblemResult(StatusCodes.Status401Unauthorized, "AUTH_SESSION_INVALID");
        }

        try
        {
            var result = await _accounts.ChangePasswordAsync(
                new ChangeIdentityPasswordCommand(
                    userEid,
                    request.CurrentPassword,
                    request.NewPassword,
                    request.ExpectedRevision,
                    new IdentityActorContext(
                        userEid,
                        IdentitySecurityActorKind.User,
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
            var response = ProblemResult(status, exception.FailureCode);
            if (FieldErrors(exception.FailureCode) is { } errors)
            {
                ((ProblemDetails)response.Value!).Extensions["errors"] = errors;
            }
            if (exception.LatestAccount is not null)
            {
                ((ProblemDetails)response.Value!).Extensions["current"] = IdentityAccountResponse.From(exception.LatestAccount);
            }

            return response;
        }
    }

    private ObjectResult ProblemResult(int status, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = code };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = StatusCode(status, problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static IReadOnlyDictionary<string, string[]>? FieldErrors(string code) => code switch
    {
        IdentityFailureCodes.InvalidPassword => Error("NewPassword", code),
        IdentityFailureCodes.InvalidCurrentCredential => Error("CurrentPassword", code),
        IdentityFailureCodes.CredentialUnchanged => Error("NewPassword", code),
        _ => null
    };

    private static IReadOnlyDictionary<string, string[]> Error(string field, string code) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] };
}

public sealed record ChangeIdentityPasswordRequest(
    string CurrentPassword,
    string NewPassword,
    long ExpectedRevision);

public sealed record IdentityPasswordChangeResponse(
    IdentityAccountResponse Account,
    [property: JsonIgnore] string SessionCredential,
    [property: JsonIgnore] DateTime SessionExpiresAtUtc) : IAuthenticationSessionCookieResponse
{
    public string GetSessionCredential() => SessionCredential;

    public DateTime GetSessionExpiresAtUtc() => SessionExpiresAtUtc;
}
