using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.ConfigCenter.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _configService;

    public ConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConfigItemDto>>> GetAllConfigs(
        [FromQuery] string? environment = null,
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var configs = await _configService.GetAllConfigsAsync(environment, tenantId, cancellationToken);
        return Ok(configs);
    }

    [HttpGet("grouped")]
    public async Task<ActionResult<List<ConfigGroupDto>>> GetConfigsGrouped(
        [FromQuery] string? environment = null,
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var groups = await _configService.GetConfigsGroupedAsync(environment, tenantId, cancellationToken);
        return Ok(groups);
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<ConfigItemDto>> GetConfig(
        string key,
        [FromQuery] string? environment = null,
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetConfigAsync(key, environment, tenantId, cancellationToken);
        if (config == null)
        {
            return NotFound();
        }
        return Ok(config);
    }

    [HttpGet("module/{moduleId}")]
    public async Task<ActionResult<List<ConfigItemDto>>> GetConfigsByModule(
        string moduleId,
        [FromQuery] string? environment = null,
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var configs = await _configService.GetConfigsByModuleAsync(moduleId, environment, tenantId, cancellationToken);
        return Ok(configs);
    }

    [HttpPost]
    public async Task<ActionResult<ConfigItemDto>> CreateConfig(
        [FromBody] ConfigItemCreateDto dto,
        CancellationToken cancellationToken = default)
    {
        var config = await _configService.CreateConfigAsync(dto, User?.Identity?.Name, cancellationToken);
        return CreatedAtAction(nameof(GetConfig), new { key = config.Key }, config);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ConfigItemDto>> UpdateConfig(
        string id,
        [FromBody] ConfigItemUpdateDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configService.UpdateConfigAsync(id, dto, User?.Identity?.Name, cancellationToken);
            return Ok(config);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteConfig(string id, CancellationToken cancellationToken = default)
    {
        await _configService.DeleteConfigAsync(id, cancellationToken);
        return NoContent();
    }
}
