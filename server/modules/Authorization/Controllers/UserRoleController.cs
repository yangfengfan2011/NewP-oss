using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/users")]
public class UserRoleController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly IAuthorizationService _authorizationService;

    public UserRoleController(IRoleService roleService, IAuthorizationService authorizationService)
    {
        _roleService = roleService;
        _authorizationService = authorizationService;
    }

    [HttpGet("{userId}/roles")]
    public async Task<ActionResult<UserRoleAssignmentsDto>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        var userRoles = await _roleService.GetUserRolesAsync(userId, cancellationToken);
        return Ok(userRoles);
    }

    [HttpPut("{userId}/roles")]
    public async Task<ActionResult> AssignUserRoles(string userId, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        await _roleService.AssignUserRolesAsync(userId, request, User.Identity?.Name, cancellationToken);
        return NoContent();
    }

    [HttpGet("{userId}/permissions")]
    public async Task<ActionResult<List<string>>> GetUserPermissions(string userId, CancellationToken cancellationToken)
    {
        var permissions = await _authorizationService.GetUserPermissionsAsync(userId, cancellationToken);
        return Ok(permissions);
    }

    [HttpPost("{userId}/check-permission")]
    public async Task<ActionResult<bool>> CheckPermission(string userId, [FromBody] CheckPermissionRequest request, CancellationToken cancellationToken)
    {
        var hasPermission = await _authorizationService.HasPermissionAsync(userId, request.PermissionCode, cancellationToken);
        return Ok(hasPermission);
    }
}

public record CheckPermissionRequest
{
    public required string PermissionCode { get; init; }
}
