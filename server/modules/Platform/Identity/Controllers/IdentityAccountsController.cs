using ICP.Platform.Persistence.Transactions;
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
[Route("api/identity/accounts")]
[EndpointModule("Identity")]
[PlatformRequestValidation]
public sealed class IdentityAccountsController : ControllerBase
{
    private readonly IIdentityManagementQueryService _queries;
    private readonly IIdentityAccountApplicationService _accounts;

    public IdentityAccountsController(
        IIdentityManagementQueryService queries,
        IIdentityAccountApplicationService accounts)
    {
        _queries = queries;
        _accounts = accounts;
    }

    /// <summary>创建账号并一次性返回凭据，调用前同时满足位置与 Host 管理资格。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.create")]
    public async Task<ActionResult<IdentityCredentialIssuanceResponse>> Create(
        [FromBody] CreateIdentityAccountRequest request,
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
                new { accountEid = result.Account.AccountEid },
                new IdentityCredentialIssuanceResponse(
                    IdentityAccountResponse.From(result.Account),
                    result.TemporaryCredential));
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return IdentityControllerProblemDetails.FromCacheException(this, exception);
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    /// <summary>读取账号及安全事件详情，沿用所属入口的读取授权。</summary>
    [HttpGet("{accountEid}")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.read")]
    public async Task<ActionResult<IdentityAccountDetailResponse>> Get(
        string accountEid,
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

        var detail = await _queries.FindAsync(accountEid, normalizedPage, normalizedPageSize, cancellationToken);
        return detail is null ? ProblemResult(404, IdentityFailureCodes.AccountNotFound) : Ok(IdentityAccountDetailResponse.From(detail));
    }

    /// <summary>
    /// 返回标准详情普通字段所需的账号快照，不读取安全事件集合。
    /// </summary>
    [HttpGet("{accountEid}/snapshot")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.read")]
    public async Task<ActionResult<IdentityAccountViewResponse>> GetSnapshot(
        string accountEid,
        CancellationToken cancellationToken)
    {
        var account = await _queries.FindAccountAsync(accountEid, cancellationToken);
        return account is null
            ? ProblemResult(404, IdentityFailureCodes.AccountNotFound)
            : Ok(IdentityAccountViewResponse.From(account));
    }

    /// <summary>
    /// 独立分页返回目标账号安全事件，供 Identity 完整详情摘要按需装载。
    /// </summary>
    [HttpGet("{accountEid}/security-events")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.read")]
    public async Task<ActionResult<IdentitySecurityEventPageResponse>> GetSecurityEvents(
        string accountEid,
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

        var events = await _queries.SearchSecurityEventsAsync(
            accountEid,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);
        return events is null
            ? ProblemResult(404, IdentityFailureCodes.AccountNotFound)
            : Ok(IdentitySecurityEventPageResponse.From(events));
    }

    /// <summary>按编辑位置修改显示名称，保留既有 Revision 校验。</summary>
    [HttpPut("{accountEid}")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.update")]
    public async Task<ActionResult<IdentityAccountResponse>> UpdateName(
        string accountEid,
        [FromBody] UpdateIdentityAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.UpdateNameAsync(
                new UpdateIdentityAccountNameCommand(accountEid, request.ENAME, request.ExpectedRevision, Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return IdentityControllerProblemDetails.FromCacheException(this, exception);
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    /// <summary>冻结旧混合入口，保留同一领域事务供独立方向动作调用。</summary>
    [HttpPut("{accountEid}/status")]
    [DenyAll("账号状态操作已由独立启用和停用位置承接。")]
    public async Task<ActionResult<IdentityAccountResponse>> SetStatus(
        string accountEid,
        [FromBody] SetIdentityAccountStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.SetStatusAsync(
                new SetIdentityAccountStatusCommand(
                    accountEid,
                    request.TargetStatus,
                    request.ExpectedRevision,
                    request.Reason,
                    Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return IdentityControllerProblemDetails.FromCacheException(this, exception);
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    /// <summary>启用位置只接受 Enabled，保留账号自身及人员绑定保护。</summary>
    [HttpPut("{accountEid}/status/enable"), Authorize(Policy = "AdminOnly"), RequireFeature("identity.account.enable")]
    public Task<ActionResult<IdentityAccountResponse>> Enable(string accountEid, [FromBody] SetIdentityAccountStatusRequest request,
        CancellationToken cancellationToken) => request.TargetStatus == IdentityAccountStatus.Enabled
            ? SetStatus(accountEid, request, cancellationToken) : Task.FromResult<ActionResult<IdentityAccountResponse>>(DirectionProblem());

    /// <summary>停用位置只接受 Disabled，复用账号停用及会话撤销事务。</summary>
    [HttpPut("{accountEid}/status/disable"), Authorize(Policy = "AdminOnly"), RequireFeature("identity.account.disable")]
    public Task<ActionResult<IdentityAccountResponse>> Disable(string accountEid, [FromBody] SetIdentityAccountStatusRequest request,
        CancellationToken cancellationToken) => request.TargetStatus == IdentityAccountStatus.Disabled
            ? SetStatus(accountEid, request, cancellationToken) : Task.FromResult<ActionResult<IdentityAccountResponse>>(DirectionProblem());

    // 参数方向沿用稳定请求校验形状，不能借用反向动作的授权。
    private BadRequestObjectResult DirectionProblem()
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["targetStatus"] = ["目标状态与当前动作不一致。"] })
        { Status = 400, Title = "REQUEST_VALIDATION_FAILED" };
        problem.Extensions["code"] = "REQUEST_VALIDATION_FAILED";
        problem.Extensions["reasonKey"] = "IdentityErrors.StatusDirectionMismatch";
        problem.Extensions["fieldErrors"] = new[]
        {
            new { field = "targetStatus", code = "REQUEST_VALIDATION_FAILED", reasonKey = "IdentityErrors.StatusDirectionMismatch" }
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = BadRequest(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    /// <summary>按删除位置执行不可恢复删除，保留账号自身保护。</summary>
    [HttpPost("{accountEid}/delete")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.delete")]
    public async Task<ActionResult<IdentityAccountResponse>> Delete(
        string accountEid,
        [FromBody] IdentityAccountSecurityCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await _accounts.DeleteAsync(
                new DeleteIdentityAccountCommand(accountEid, request.ExpectedRevision, request.Reason, Actor()),
                cancellationToken);
            return Ok(IdentityAccountResponse.From(account));
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return IdentityControllerProblemDetails.FromCacheException(this, exception);
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    /// <summary>按重置位置签发一次性凭据，保留本地管理员维护边界。</summary>
    [HttpPost("{accountEid}/credential-reset")]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.credentialReset")]
    public async Task<ActionResult<IdentityCredentialIssuanceResponse>> ResetCredential(
        string accountEid,
        [FromBody] IdentityAccountSecurityCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _accounts.ResetCredentialAsync(
                new ResetIdentityCredentialCommand(accountEid, request.ExpectedRevision, request.Reason, Actor()),
                cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Ok(new IdentityCredentialIssuanceResponse(
                IdentityAccountResponse.From(result.Account),
                result.TemporaryCredential));
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return IdentityControllerProblemDetails.FromCacheException(this, exception);
        }
        catch (IdentityApplicationException exception)
        {
            return ApplicationProblem(exception);
        }
    }

    /// <summary>
    /// 查询账号目录；同时接受既有 page 与标准宿主 pageIndex，并把允许的排序意图交给 Identity 查询服务。
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("identity.account.read")]
    public async Task<ActionResult<IdentityAccountPageResponse>> List(
        [FromQuery] string? query,
        [FromQuery] IdentityAccountStatus? status,
        [FromQuery] bool? isPersonnelBound,
        [FromQuery] bool? isLocalAdministrator,
        [FromQuery] int? page,
        [FromQuery] int? pageIndex,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken)
    {
        var normalizedPage = pageIndex ?? page ?? 1;
        var normalizedPageSize = pageSize ?? 20;
        if (normalizedPage < 1 || normalizedPageSize is < 1 or > 100)
        {
            return ProblemResult(400, "IDENTITY_QUERY_INVALID");
        }

        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim();
        var normalizedSortDirection = string.IsNullOrWhiteSpace(sortDirection)
            ? null
            : sortDirection.Trim().ToLowerInvariant();
        if ((normalizedSortBy is not null
                && normalizedSortBy is not ("ECODE" or "ENAME" or "Status"))
            || (normalizedSortDirection is not null
                && normalizedSortDirection is not ("asc" or "desc")))
        {
            return ProblemResult(400, "IDENTITY_QUERY_INVALID");
        }

        var result = await _queries.SearchAsync(
            new IdentityManagementAccountSearch(
                string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                status,
                isPersonnelBound,
                isLocalAdministrator,
                normalizedPage,
                normalizedPageSize,
                normalizedSortBy,
                normalizedSortDirection),
            cancellationToken);
        return Ok(IdentityAccountPageResponse.From(result));
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

        return new IdentityActorContext(actorId, IdentitySecurityActorKind.IdentityAccount, HttpContext.TraceIdentifier);
    }
}

public sealed record CreateIdentityAccountRequest(string ECODE, string ENAME) : IdentityAccountMutationRequest;

public sealed record UpdateIdentityAccountRequest(
    string ENAME,
    [Range(0, long.MaxValue)] long ExpectedRevision) : IdentityAccountMutationRequest;

/// <summary>账号写协议拒绝服务端审计字段和输出 companion；未知业务扩展仍保持既有忽略语义。</summary>
public abstract record IdentityAccountMutationRequest : IValidatableObject
{
    private static readonly IReadOnlySet<string> AuditFields = new ICP.Platform.Modeling.Projection.ModelMetadataProjector()
        .ProjectFixedFields(new ICP.Platform.Modeling.Metadata.ModelMetadataDocument
        {
            Model = new ICP.Platform.Modeling.Metadata.ModelMetadataModel { Kind = "Entity" }
        }).Where(field => field.IsAuditField).Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        (ExtraFields?.Keys.AsEnumerable() ?? []).Where(field => AuditFields.Contains(field)
            || field.EndsWith("_DisplayValue", StringComparison.OrdinalIgnoreCase))
            .Select(field => new ValidationResult("IDENTITY_OUTPUT_FIELD_READ_ONLY", [field]));
}

public sealed record SetIdentityAccountStatusRequest(
    IdentityAccountStatus TargetStatus,
    [Range(0, long.MaxValue)] long ExpectedRevision,
    string Reason);

public sealed record IdentityAccountSecurityCommandRequest(
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
        account.AccountEid,
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
    string? OccurredAt_DisplayValue,
    string TraceId,
    IdentitySecurityActorKind ActorKind,
    string? ActorAccountEID,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt)
{
    /// <summary>将应用层安全事件视图投影为稳定 API response。</summary>
    internal static IdentitySecurityEventResponse From(IdentityManagementSecurityEventView view) => new(
        view.EventEid,
        view.EventType,
        view.Reason,
        view.OccurredAt,
        view.OccurredAtDisplayValue,
        view.TraceId,
        view.ActorKind,
        view.ActorAccountEid,
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

public sealed record IdentityAccountDetailResponse(
    IdentityAccountViewResponse Account,
    IdentitySecurityEventPageResponse Events)
{
    internal static IdentityAccountDetailResponse From(IdentityManagementAccountDetail detail) => new(
        IdentityAccountViewResponse.From(detail.Account),
        IdentitySecurityEventPageResponse.From(detail.Events));
}

public sealed record IdentityAccountViewResponse(
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
    [System.Text.Json.Serialization.JsonPropertyName("CreatedBy_DisplayValue")]
    public string? CreatedBy_DisplayValue { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("CreatedAt_DisplayValue")]
    public string? CreatedAt_DisplayValue { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("UpdatedBy_DisplayValue")]
    public string? UpdatedBy_DisplayValue { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("UpdatedAt_DisplayValue")]
    public string? UpdatedAt_DisplayValue { get; init; }

    // 只转换既有公开协议字段，保留公共 CRUD 已生成的伴随显示值。
    internal static IdentityAccountViewResponse From(IdentityManagementAccountView view) => new(
        view.AccountEid,
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
        view.UpdatedAt)
    {
        CreatedBy_DisplayValue = view.CreatedBy_DisplayValue,
        CreatedAt_DisplayValue = view.CreatedAt_DisplayValue,
        UpdatedBy_DisplayValue = view.UpdatedBy_DisplayValue,
        UpdatedAt_DisplayValue = view.UpdatedAt_DisplayValue,
    };
}

public sealed record IdentityAccountPageResponse(
    IReadOnlyList<IdentityAccountViewResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int Total => TotalCount;

    internal static IdentityAccountPageResponse From(IdentityManagementAccountPage page) => new(
        page.Items.Select(IdentityAccountViewResponse.From).ToArray(),
        page.Page,
        page.PageSize,
        page.TotalCount);
}
