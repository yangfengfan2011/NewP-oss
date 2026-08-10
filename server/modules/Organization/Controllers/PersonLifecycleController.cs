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
public sealed class PersonLifecycleController(IPersonLifecycleService lifecycleService) : ControllerBase
{
    [HttpPut("{personEid}/status")]
    [RequirePermission(OrganizationPermissions.PersonUpdate)]
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
