using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/permissions")]
public class PermissionController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public PermissionController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<PermissionDto>>> GetAllPermissions(CancellationToken cancellationToken)
    {
        var permissions = await _permissionService.GetAllPermissionsAsync(cancellationToken);
        return Ok(permissions);
    }

    [HttpGet("grouped")]
    public async Task<ActionResult<List<PermissionGroupDto>>> GetPermissionsByModule(CancellationToken cancellationToken)
    {
        var groups = await _permissionService.GetPermissionsByModuleAsync(cancellationToken);
        return Ok(groups);
    }
}
