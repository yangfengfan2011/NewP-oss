using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/initialization")]
[EndpointModule("Organization")]
public sealed class OrganizationInitializationController(IButtonBusinessExecutor buttons, OrganizationInitializationButtonHandler handler) : ControllerBase
{
    /// <summary>只有可信内置管理员可见初始化动作；普通授权不会提升该维护身份。</summary>
    [HttpGet("eligibility")]
    [RequireFeature("organization.company.initialize")]
    public ActionResult<OrganizationInitializationEligibility> Eligibility() => Ok(new OrganizationInitializationEligibility(User.IsInRole(LocalAdministratorRole.Code)));

    /// <summary>在管理员和功能双重资格下显式执行无请求载荷的组织初始化。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [RequireFeature("organization.company.initialize"), ButtonBusinessHandler(typeof(OrganizationInitializationButtonHandler))]
    public async Task<ActionResult<OrganizationInitializationResult>> Initialize(CancellationToken cancellationToken = default)
    {
        try { return Ok(await buttons.ExecuteAsync(handler, new OrganizationInitializationButtonInput(), cancellationToken)); }
        catch (ActionDataAccessException exception) { return ActionDataAccessProblem.ToResult(exception, exception.Code); }
        catch (OrganizationInitializationConflictException)
        {
            var problem = new ProblemDetails { Status = 409, Title = "ORGANIZATION_INITIALIZATION_CONFLICT" };
            problem.Extensions["code"] = "ORGANIZATION_INITIALIZATION_CONFLICT";
            problem.Extensions["reasonKey"] = "CompanyListView.InitializationConflict";
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return Conflict(problem);
        }
    }
}

/// <summary>当前账号是否具备组织初始化所需的独立维护身份。</summary>
public sealed record OrganizationInitializationEligibility(bool Allowed);
