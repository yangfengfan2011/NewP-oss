using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ICP.Platform.HostApi.Readiness;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly IConfiguration? _configuration;

    public HealthController(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "0.1.0"
        });
    }

    [HttpGet("ready")]
    [ProducesResponseType<ReadinessResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReadinessResponseDto>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReady(
        [FromServices] IReadinessEvaluator readinessEvaluator,
        CancellationToken cancellationToken)
    {
        var evaluation = await readinessEvaluator.EvaluateAsync(cancellationToken);
        var response = evaluation.Response with
        {
            Supervisor = CreateSupervisorIdentity(_configuration)
        };

        return evaluation.IsReady
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    private static DevSupervisorIdentityDto? CreateSupervisorIdentity(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        var repositoryId = configuration["DevSupervisor:RepositoryId"];
        var worktreeId = configuration["DevSupervisor:WorktreeId"];
        var runId = configuration["DevSupervisor:RunId"];
        var endpointTokenFingerprint = configuration["DevSupervisor:EndpointTokenFingerprint"];
        if (string.IsNullOrWhiteSpace(repositoryId)
            || string.IsNullOrWhiteSpace(worktreeId)
            || string.IsNullOrWhiteSpace(runId)
            || string.IsNullOrWhiteSpace(endpointTokenFingerprint))
        {
            return null;
        }

        return new DevSupervisorIdentityDto(
            repositoryId,
            worktreeId,
            runId,
            endpointTokenFingerprint);
    }
}
