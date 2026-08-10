using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Identity.Controllers;

[ApiController]
[Route("api/identity/users")]
[Authorize(Policy = "AdminOnly")]
[PlatformRequestValidation]
public sealed class IdentityUsersController : ControllerBase
{
    private readonly IIdentityManagementQueryService _queries;
    private readonly IIdentityAccountApplicationService _accounts;

    public IdentityUsersController(
        IIdentityManagementQueryService queries,
        IIdentityAccountApplicationService accounts)
    {
        _queries = queries;
        _accounts = accounts;
    }

    [HttpPost]
    public async Task<ActionResult<IdentityCredentialIssuanceResponse>> Create(
        [FromBody] CreateIdentityUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _accounts.CreateAsync(
                new CreateIdentityAccountCommand(request.ECODE, request.ENAME, Actor()),
                cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return CreatedAtAction(
                nameof(Get),
                new { userEid = result.Account.UserEid },
                new IdentityCredentialIssuanceResponse(
                    IdentityAccountResponse.From(result.Account),
                    result.TemporaryCredential));
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    [HttpGet("{userEid}")]
    public async Task<ActionResult<IdentityUserDetailResponse>> Get(
        string userEid,
        [FromQuery] int? eventPage,
        [FromQuery] int? eventPageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = eventPage ?? 1;
        var normalizedPageSize = eventPageSize ?? 20;
        if (normalizedPage < 1 || normalizedPageSize is < 1 or > 100)
        {
            return ProblemResult(400, "IDENTITY_QUERY_INVALID");
        }

        var detail = await _queries.FindAsync(userEid, normalizedPage, normalizedPageSize, cancellationToken);
        return detail is null ? ProblemResult(404, IdentityFailureCodes.AccountNotFound) : Ok(IdentityUserDetailResponse.From(detail));
    }

    [HttpPut("{userEid}")]
    public async Task<ActionResult<IdentityAccountResponse>> UpdateName(
        string userEid,
        [FromBody] UpdateIdentityUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.UpdateNameAsync(
                new UpdateIdentityAccountNameCommand(userEid, request.ENAME, request.ExpectedRevision, Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    [HttpPut("{userEid}/status")]
    public async Task<ActionResult<IdentityAccountResponse>> SetStatus(
        string userEid,
        [FromBody] SetIdentityUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.SetStatusAsync(
                new SetIdentityAccountStatusCommand(
                    userEid,
                    request.TargetStatus,
                    request.ExpectedRevision,
                    request.Reason,
                    Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    [HttpPost("{userEid}/delete")]
    public async Task<ActionResult<IdentityAccountResponse>> Delete(
        string userEid,
        [FromBody] IdentityUserSecurityCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.DeleteAsync(
                new DeleteIdentityAccountCommand(userEid, request.ExpectedRevision, request.Reason, Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    [HttpPost("{userEid}/credential-reset")]
    public async Task<ActionResult<IdentityCredentialIssuanceResponse>> ResetCredential(
        string userEid,
        [FromBody] IdentityUserSecurityCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _accounts.ResetCredentialAsync(
                new ResetIdentityCredentialCommand(userEid, request.ExpectedRevision, request.Reason, Actor()),
                cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Ok(new IdentityCredentialIssuanceResponse(
                IdentityAccountResponse.From(result.Account),
                result.TemporaryCredential));
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IdentityUserPageResponse>> List(
        [FromQuery] string? query,
        [FromQuery] IdentityAccountStatus? status,
        [FromQuery] bool? isPersonnelBound,
        [FromQuery] bool? isLocalAdministrator,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = page ?? 1;
        var normalizedPageSize = pageSize ?? 20;
        if (normalizedPage < 1 || normalizedPageSize is < 1 or > 100)
        {
            return ProblemResult(400, "IDENTITY_QUERY_INVALID");
        }

        var result = await _queries.SearchAsync(
            new IdentityManagementUserSearch(
                string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                status,
                isPersonnelBound,
                isLocalAdministrator,
                normalizedPage,
                normalizedPageSize),
            cancellationToken);
        return Ok(IdentityUserPageResponse.From(result));
    }

    private ObjectResult ProblemResult(int status, string code)
        => IdentityControllerProblemDetails.Create(this, status, code);

    private ObjectResult ApplicationProblem(IdentityApplicationException exception)
    {
        var status = exception.FailureCode switch
        {
            IdentityFailureCodes.AccountNotFound => StatusCodes.Status404NotFound,
            IdentityFailureCodes.RevisionConflict or IdentityFailureCodes.AccountCodeConflict => StatusCodes.Status409Conflict,
            IdentityFailureCodes.DeletedAccount
                or IdentityFailureCodes.LocalAdministratorProtected
                or IdentityFailureCodes.PersonnelBound
                or IdentityFailureCodes.PersonnelDisableHold => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return IdentityControllerProblemDetails.FromApplicationException(this, status, exception);
    }

    private IdentityActorContext Actor()
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new IdentityApplicationException(IdentityFailureCodes.InvalidActor);
        }

        return new IdentityActorContext(actorId, IdentitySecurityActorKind.User, HttpContext.TraceIdentifier);
    }
}

public sealed record CreateIdentityUserRequest(string ECODE, string ENAME);

public sealed record UpdateIdentityUserRequest(
    string ENAME,
    [Range(0, long.MaxValue)] long ExpectedRevision);

public sealed record SetIdentityUserStatusRequest(
    IdentityAccountStatus TargetStatus,
    [Range(0, long.MaxValue)] long ExpectedRevision,
    string Reason);

public sealed record IdentityUserSecurityCommandRequest(
    [Range(0, long.MaxValue)] long ExpectedRevision,
    string Reason);

public sealed record IdentityAccountResponse(
    string EID,
    string ECODE,
    string ENAME,
    IdentityAccountStatus Status,
    long Revision,
    bool IsLocalAdministrator,
    bool IsPersonnelBound,
    bool PersonnelDisableHold)
{
    internal static IdentityAccountResponse From(IdentityAccountSnapshot account) => new(
        account.UserEid,
        account.ECODE,
        account.ENAME,
        account.Status,
        account.Revision,
        account.IsLocalAdministrator,
        account.IsPersonnelBound,
        account.PersonnelDisableHold);
}

public sealed record IdentityCredentialIssuanceResponse(
    IdentityAccountResponse Account,
    string TemporaryCredential);

public sealed record IdentitySecurityEventResponse(
    string EID,
    string EventType,
    string Reason,
    DateTime OccurredAt,
    string TraceId,
    IdentitySecurityActorKind ActorKind,
    string? ActorUserEID,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt)
{
    internal static IdentitySecurityEventResponse From(IdentityManagementSecurityEventView view) => new(
        view.EventEid,
        view.EventType,
        view.Reason,
        view.OccurredAt,
        view.TraceId,
        view.ActorKind,
        view.ActorUserEid,
        view.CreatedBy,
        view.CreatedAt,
        view.UpdatedBy,
        view.UpdatedAt);
}

public sealed record IdentitySecurityEventPageResponse(
    IReadOnlyList<IdentitySecurityEventResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    internal static IdentitySecurityEventPageResponse From(IdentityManagementSecurityEventPage page) => new(
        page.Items.Select(IdentitySecurityEventResponse.From).ToArray(),
        page.Page,
        page.PageSize,
        page.TotalCount);
}

public sealed record IdentityUserDetailResponse(
    IdentityUserResponse Account,
    IdentitySecurityEventPageResponse Events)
{
    internal static IdentityUserDetailResponse From(IdentityManagementUserDetail detail) => new(
        IdentityUserResponse.From(detail.Account),
        IdentitySecurityEventPageResponse.From(detail.Events));
}

public sealed record IdentityUserResponse(
    string EID,
    string ECODE,
    string ENAME,
    IdentityAccountStatus Status,
    long Revision,
    bool IsLocalAdministrator,
    bool IsPersonnelBound,
    bool PersonnelDisableHold,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt)
{
    internal static IdentityUserResponse From(IdentityManagementUserView view) => new(
        view.UserEid,
        view.ECODE,
        view.ENAME,
        view.Status,
        view.Revision,
        view.IsLocalAdministrator,
        view.IsPersonnelBound,
        view.PersonnelDisableHold,
        view.CreatedBy,
        view.CreatedAt,
        view.UpdatedBy,
        view.UpdatedAt);
}

public sealed record IdentityUserPageResponse(
    IReadOnlyList<IdentityUserResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    internal static IdentityUserPageResponse From(IdentityManagementUserPage page) => new(
        page.Items.Select(IdentityUserResponse.From).ToArray(),
        page.Page,
        page.PageSize,
        page.TotalCount);
}
