using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.ConfigCenter.Controllers;

[ApiController]
[Route("api/value-lists")]
public class ValueListController : ControllerBase
{
    private readonly IValueListService _service;

    public ValueListController(IValueListService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<ValueListItemDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var items = await _service.GetAllAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<ValueListItemDto>> Create(
        [FromBody] ValueListItemDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _service.CreateAsync(dto, User?.Identity?.Name, cancellationToken);
            return Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ValueListItemDto>> Update(
        string id,
        [FromBody] ValueListItemDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _service.UpdateAsync(id, dto, User?.Identity?.Name, cancellationToken);
            return Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
