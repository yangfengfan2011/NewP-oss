using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/positions")]
public sealed class PositionsController : ControllerBase
{
    private readonly IPositionService _positionService;

    public PositionsController(IPositionService positionService)
    {
        _positionService = positionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<PositionDto>>> GetByDepartment(
        [FromQuery] string departmentId,
        CancellationToken cancellationToken = default)
    {
        var positions = await _positionService.GetByDepartmentIdAsync(departmentId, cancellationToken);
        return Ok(positions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PositionDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        var position = await _positionService.GetByIdAsync(id, cancellationToken);
        return position is null ? NotFound() : Ok(position);
    }

    [HttpPost]
    public async Task<ActionResult<PositionDto>> Create(
        [FromBody] PositionDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var position = await _positionService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = position.Id }, position);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PositionDto>> Update(
        string id,
        [FromBody] PositionDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var position = await _positionService.UpdateAsync(id, dto, cancellationToken);
            return Ok(position);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
