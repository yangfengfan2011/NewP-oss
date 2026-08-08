using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/permissions")]
[EndpointModule("Authorization")]
public class PermissionController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public PermissionController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    [RequirePermission(AuthorizationPermissions.PermissionView)]
    public async Task<ActionResult<List<PermissionDto>>> GetAllPermissions(CancellationToken cancellationToken)
    {
        var permissions = await _permissionService.GetAllPermissionsAsync(cancellationToken);
        return Ok(permissions);
    }

    [HttpGet("grouped")]
    [RequirePermission(AuthorizationPermissions.PermissionView)]
    public async Task<ActionResult<List<PermissionGroupDto>>> GetPermissionsByModule(CancellationToken cancellationToken)
    {
        var groups = await _permissionService.GetPermissionsByModuleAsync(cancellationToken);
        return Ok(groups);
    }
}
