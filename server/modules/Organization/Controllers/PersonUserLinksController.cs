using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
[EndpointModule("Organization")]
public sealed class PersonUserLinksController : ControllerBase
{
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IPersonUserLinkService _personUserLinkService;

    public PersonUserLinksController(
        IIdentityUserDirectory identityUserDirectory,
        IPersonUserLinkService personUserLinkService)
    {
        _identityUserDirectory = identityUserDirectory;
        _personUserLinkService = personUserLinkService;
    }

    [HttpGet("user-options")]
    [RequirePermission(OrganizationPermissions.PersonLinkUser)]
    public async Task<ActionResult<IReadOnlyList<IdentityUserOptionDto>>> SearchUserOptions(
        [FromQuery] string keyword,
        CancellationToken cancellationToken = default)
    {
        var options = await _identityUserDirectory.SearchAsync(keyword, cancellationToken);
        return Ok(options);
    }

    [HttpPut("{id}/user-link")]
    [RequirePermission(OrganizationPermissions.PersonLinkUser)]
    public async Task<ActionResult<PersonUserLinkDto>> LinkUser(
        string id,
        [FromBody] LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _personUserLinkService.LinkUserAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
