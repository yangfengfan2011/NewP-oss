using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.MetadataConfigCenter.Controllers;

[ApiController]
[Route("api/metadata-config/modules/{moduleId}/settings")]
public class SettingDefinitionsController : ControllerBase
{
    private readonly ISettingDefinitionAuthoringService _settingDefinitionAuthoringService;
    private readonly ISettingDefinitionApplyService _settingDefinitionApplyService;

    public SettingDefinitionsController(
        ISettingDefinitionAuthoringService settingDefinitionAuthoringService,
        ISettingDefinitionApplyService settingDefinitionApplyService)
    {
        _settingDefinitionAuthoringService = settingDefinitionAuthoringService;
        _settingDefinitionApplyService = settingDefinitionApplyService;
    }

    [HttpGet]
    public async Task<ActionResult<List<SettingDefinitionDto>>> GetSettings(string moduleId, CancellationToken cancellationToken = default)
    {
        var definitions = await _settingDefinitionAuthoringService.GetSettingDefinitionsAsync(moduleId, cancellationToken);
        return Ok(definitions);
    }

    [HttpPut]
    public async Task<ActionResult<SaveSettingDefinitionsResult>> SaveSettings(
        string moduleId,
        [FromBody] SaveSettingDefinitionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _settingDefinitionAuthoringService.SaveSettingDefinitionsAsync(moduleId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("apply")]
    public async Task<ActionResult<ApplySettingDefinitionsResult>> ApplySettings(string moduleId, CancellationToken cancellationToken = default)
    {
        var result = await _settingDefinitionApplyService.ApplySettingDefinitionsAsync(moduleId, cancellationToken);
        return Ok(result);
    }
}
