using ICP.Platform.ExternalPluginTemplate.DTOs;
using ICP.Platform.ExternalPluginTemplate.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.ExternalPluginTemplate.Controllers;

[ApiController]
[Route("api/external-plugin/status")]
public class ExternalPluginStatusController : ControllerBase
{
    private readonly IExternalPluginStatusService _statusService;

    public ExternalPluginStatusController(IExternalPluginStatusService statusService)
    {
        _statusService = statusService;
    }

    [HttpGet]
    public ActionResult<ExternalPluginStatusResponse> GetStatus()
    {
        return Ok(_statusService.GetStatus());
    }
}
