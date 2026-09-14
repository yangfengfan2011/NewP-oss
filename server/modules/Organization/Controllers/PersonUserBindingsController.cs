using System.Security.Claims;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
[EndpointModule("Organization")]
[PlatformRequestValidation]
public sealed class PersonUserBindingsController(IPersonUserBindingService bindingService) : ControllerBase
{
    /// <summary>绑定动作按其已授权位置搜索候选账号。</summary>
    [HttpGet("{personEid}/user-binding/candidates")]
    [RequireFeature("organization.person.linkUser.lookup")]
    public async Task<ActionResult<IReadOnlyList<PersonUserBindingCandidateDto>>> SearchCandidates(
        string personEid,
        [FromQuery] string? query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await bindingService.SearchCandidatesAsync(personEid, query, cancellationToken));
        }
        catch (PersonUserBindingException exception)
        {
            return BindingProblem(exception);
        }
    }

    /// <summary>在可信主体的绑定动作授权后执行既有不可逆绑定事务。</summary>
    [HttpPut("{personEid}/user-binding")]
    [RequireFeature("organization.person.linkUser")]
    public async Task<ActionResult<PersonUserBindingDto>> Bind(
        string personEid,
        [FromBody] BindPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidActor);
            }

            return Ok(await bindingService.BindAsync(
                personEid,
                request,
                new PersonUserBindingActor(actorId, HttpContext.TraceIdentifier),
                cancellationToken));
        }
        catch (PersonUserBindingException exception)
        {
            return BindingProblem(exception);
        }
    }

    private ObjectResult BindingProblem(PersonUserBindingException exception)
    {
        var status = exception.FailureCode switch
        {
            PersonUserBindingFailureCodes.PersonNotFound => StatusCodes.Status404NotFound,
            PersonUserBindingFailureCodes.IdentityUnavailable => StatusCodes.Status503ServiceUnavailable,
            PersonUserBindingFailureCodes.PersonDisabled
                or PersonUserBindingFailureCodes.PersonAlreadyBound
                or PersonUserBindingFailureCodes.UserAlreadyBound
                or PersonUserBindingFailureCodes.LocalAdministratorProtected
                or PersonUserBindingFailureCodes.RevisionConflict => StatusCodes.Status409Conflict,
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
