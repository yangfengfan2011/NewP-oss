using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[ServiceFilter(typeof(LocalAdministratorManagementFilter))]
[Route("api/authorization/users")]
[EndpointModule("Authorization")]
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
    [RequirePermission(AuthorizationPermissions.RoleView)]
    public async Task<ActionResult<UserRoleAssignmentsDto>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        var userRoles = await _roleService.GetUserRolesAsync(userId, cancellationToken);
        return Ok(userRoles);
    }

    [HttpPut("{userId}/roles")]
    [RequirePermission(AuthorizationPermissions.UserAssignRole)]
    public async Task<ActionResult> AssignUserRoles(string userId, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        await _roleService.AssignUserRolesAsync(userId, request, User.Identity?.Name, cancellationToken);
        return NoContent();
    }

    [HttpGet("{userId}/permissions")]
    [DenyAll("Arbitrary-user permission projection is not a supported runtime interface.")]
    public async Task<ActionResult<List<string>>> GetUserPermissions(string userId, CancellationToken cancellationToken)
    {
        var permissions = await _authorizationService.GetUserPermissionsAsync(userId, cancellationToken);
        return Ok(permissions);
    }

    [HttpPost("{userId}/check-permission")]
    [DenyAll("Arbitrary-user permission checks must use the server-trusted current subject.")]
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
