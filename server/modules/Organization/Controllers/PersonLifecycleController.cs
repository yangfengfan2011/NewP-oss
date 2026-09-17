using System.Security.Claims;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
[EndpointModule("Organization")]
[PlatformRequestValidation]
public sealed class PersonLifecycleController(IPersonLifecycleService lifecycleService) : ControllerBase
{
    /// <summary>旧的混合状态入口显式冻结，状态改变由独立动作绑定拥有。</summary>
    [HttpPut("{personEid}/status")]
    [DenyAll("人员状态操作已由独立启用和停用位置承接。")]
    public async Task<ActionResult<PersonLifecycleDto>> SetStatus(
        string personEid,
        [FromBody] SetPersonLifecycleStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
            }

            return Ok(await lifecycleService.SetStatusAsync(
                personEid,
                request,
                new PersonLifecycleActor(actorId, HttpContext.TraceIdentifier),
                cancellationToken));
        }
        catch (PersonLifecycleException exception)
        {
            return LifecycleProblem(exception);
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return OrganizationCacheProblem.Create(exception, HttpContext.TraceIdentifier);
        }
    }

    /// <summary>启用动作只允许启用参数；不能借用该位置提交停用。</summary>
    [HttpPut("{personEid}/status/enable")]
    [RequireFeature("organization.person.enable")]
    public Task<ActionResult<PersonLifecycleDto>> Enable(string personEid, [FromBody] SetPersonLifecycleStatusRequest request,
        CancellationToken cancellationToken = default) => request.IsEnabled ? SetStatus(personEid, request, cancellationToken)
            : Task.FromResult<ActionResult<PersonLifecycleDto>>(DirectionProblem("启用动作要求 true。"));

    /// <summary>停用动作只允许停用参数，保留原领域事务与会话撤销逻辑。</summary>
    [HttpPut("{personEid}/status/disable")]
    [RequireFeature("organization.person.disable")]
    public Task<ActionResult<PersonLifecycleDto>> Disable(string personEid, [FromBody] SetPersonLifecycleStatusRequest request,
        CancellationToken cancellationToken = default) => !request.IsEnabled ? SetStatus(personEid, request, cancellationToken)
            : Task.FromResult<ActionResult<PersonLifecycleDto>>(DirectionProblem("停用动作要求 false。"));

    // 动作方向是 HTTP 输入约束，沿用平台请求校验的稳定错误和字段定位。
    private BadRequestObjectResult DirectionProblem(string message)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["isEnabled"] = [message] })
        { Status = StatusCodes.Status400BadRequest, Title = "REQUEST_VALIDATION_FAILED" };
        problem.Extensions["code"] = "REQUEST_VALIDATION_FAILED";
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = BadRequest(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
    private ObjectResult LifecycleProblem(PersonLifecycleException exception)
    {
        var status = exception.FailureCode switch
        {
            PersonLifecycleFailureCodes.PersonNotFound => StatusCodes.Status404NotFound,
            PersonLifecycleFailureCodes.Conflict => StatusCodes.Status409Conflict,
            PersonLifecycleFailureCodes.DependencyUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        var problem = new ProblemDetails { Status = status, Title = exception.FailureCode };
        problem.Extensions["code"] = exception.FailureCode;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        if (exception.Current is not null)
        {
            problem.Extensions["current"] = exception.Current;
        }

        var result = StatusCode(status, problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
