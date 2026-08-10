using ICP.Platform.Authorization.Http;
using ICP.Platform.Templates.BackendModuleTemplate.DTOs;
using ICP.Platform.Templates.BackendModuleTemplate.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Templates.BackendModuleTemplate.Controllers;

[ApiController]
[Route("api/template-module")]
[RequirePermission("TemplateModule.View")]
public class TemplateController : ControllerBase
{
    private readonly ITemplateService _service;

    public TemplateController(ITemplateService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    public ActionResult<TemplateResponse> GetStatus()
    {
        return Ok(new TemplateResponse
        {
            Message = _service.GetStatusMessage()
        });
    }
}
