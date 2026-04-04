using Microsoft.AspNetCore.Mvc;

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
}
