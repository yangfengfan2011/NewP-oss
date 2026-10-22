using ICP.Platform.Authorization.Http;
using ICP.Platform.Templates.BackendModuleTemplate.DTOs;
using ICP.Platform.Templates.BackendModuleTemplate.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Templates.BackendModuleTemplate.Controllers;

[ApiController]
[Route("api/template-module")]
[EndpointModule("TemplateModule")]
[RequireFeature("template-module.read")]
public class TemplateController : ControllerBase
{
    private readonly ITemplateService _service;

    /// <summary>控制器只组合模块服务，功能位置由正式端点规则校验。</summary>
    public TemplateController(ITemplateService service)
    {
        _service = service;
    }

    /// <summary>读取模块状态；业务读取行为与导航入口共享声明。</summary>
    [HttpGet("status")]
    public ActionResult<TemplateResponse> GetStatus()
    {
        return Ok(new TemplateResponse
        {
            Message = _service.GetStatusMessage()
        });
    }
}
