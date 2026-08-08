using Microsoft.AspNetCore.Mvc;
using ICP.Platform.HostApi.Readiness;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
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
        return evaluation.IsReady
            ? Ok(evaluation.Response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, evaluation.Response);
    }
}
