using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.ModuleTemplateSample.DTOs;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.ModuleTemplateSample.Controllers;

[ApiController]
[Route("api/module-template-sample")]
[DenyAll("Module template sample endpoints are not production capabilities.")]
public class SampleStatusController : ControllerBase
{
    private readonly ISampleStatusService _service;

    public SampleStatusController(ISampleStatusService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    public ActionResult<SampleStatusResponse> GetStatus()
    {
        return Ok(new SampleStatusResponse
        {
            ModuleId = "ModuleTemplateSample",
            DisplayName = "模块模板示例",
            Message = _service.GetStatusMessage()
        });
    }
}
