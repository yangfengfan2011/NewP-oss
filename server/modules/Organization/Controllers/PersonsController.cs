using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;
    private readonly IIdentityUserDirectory _identityUserDirectory;

    public PersonsController(IPersonService personService, IIdentityUserDirectory identityUserDirectory)
    {
        _personService = personService;
        _identityUserDirectory = identityUserDirectory;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PersonListItemDto>>> GetList(
        [FromQuery] string? keyword,
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? departmentId,
        [FromQuery] EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default)
    {
        var persons = await _personService.GetListAsync(keyword, companyId, departmentId, employmentStatus, cancellationToken);
        return Ok(persons);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonDetailDto>> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await _personService.GetDetailAsync(id, cancellationToken);
        return person is null ? NotFound() : Ok(person);
    }

    [HttpPost]
    public async Task<ActionResult<PersonDto>> Create(
        [FromBody] PersonDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var person = await _personService.CreateAsync(dto, cancellationToken);
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
        [FromBody] PersonDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var person = await _personService.UpdateAsync(id, dto, cancellationToken);
            return Ok(person);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<PersonDetailDto>> Disable(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await _personService.DisableAsync(id, cancellationToken);
            return Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("user-options")]
    public async Task<ActionResult<IReadOnlyList<IdentityUserOptionDto>>> SearchUserOptions(
        [FromQuery] string keyword,
        CancellationToken cancellationToken = default)
    {
        var options = await _identityUserDirectory.SearchAsync(keyword, cancellationToken);
        return Ok(options);
    }

    [HttpPut("{id:guid}/user-link")]
    public async Task<ActionResult<PersonUserLinkDto>> LinkUser(
        Guid id,
        [FromBody] LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _personService.LinkUserAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
