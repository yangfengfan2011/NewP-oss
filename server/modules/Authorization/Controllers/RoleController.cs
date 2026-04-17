using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/roles")]
public class RoleController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RoleController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> GetAllRoles(CancellationToken cancellationToken)
    {
        var roles = await _roleService.GetAllRolesAsync(cancellationToken);
        return Ok(roles);
    }

    [HttpGet("{roleId}")]
    public async Task<ActionResult<RoleDto>> GetRoleById(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleService.GetRoleByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            return NotFound();
        }
        return Ok(role);
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.CreateRoleAsync(dto, User.Identity?.Name, cancellationToken);
            return CreatedAtAction(nameof(GetRoleById), new { roleId = role.Id }, role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{roleId}")]
    public async Task<ActionResult<RoleDto>> UpdateRole(string roleId, [FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.UpdateRoleAsync(roleId, dto, User.Identity?.Name, cancellationToken);
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{roleId}")]
    public async Task<ActionResult> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.DeleteRoleAsync(roleId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{roleId}/permissions")]
    public async Task<ActionResult<RolePermissionsDto>> GetRolePermissions(string roleId, CancellationToken cancellationToken)
    {
        var permissions = await _roleService.GetRolePermissionsAsync(roleId, cancellationToken);
        return Ok(permissions);
    }

    [HttpPut("{roleId}/permissions")]
    public async Task<ActionResult> AssignRolePermissions(string roleId, [FromBody] AssignRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.AssignRolePermissionsAsync(roleId, request, User.Identity?.Name, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
