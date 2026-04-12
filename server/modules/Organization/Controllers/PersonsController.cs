using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;

    public PersonsController(IPersonService personService)
    {
        _personService = personService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonDetailDto>> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await _personService.GetDetailAsync(id, cancellationToken);
        return person is null ? NotFound() : Ok(person);
    }

    [HttpPost]
    public async Task<ActionResult<PersonDto>> Create(
        [FromBody] CreatePersonRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var person = await _personService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = person.Id }, person);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PersonDto>> Update(
        Guid id,
        [FromBody] UpdatePersonRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var person = await _personService.UpdateAsync(id, request, cancellationToken);
            return Ok(person);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
