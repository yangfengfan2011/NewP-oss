using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/roles")]
[EndpointModule("Authorization")]
public class RoleController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly RoleManagementQuery _query;

    // 组合普通命令与有界查询，管理动作由各自的位置准入。
    public RoleController(IRoleService roleService, RoleManagementQuery query)
    {
        _roleService = roleService;
        _query = query;
    }

    /// <summary>标准列表在存储侧按代码筛选及分页，旧列表 API 保持原协议。</summary>
    [HttpGet("query")]
    [RequireFeature("authorization.role.read")]
    public async Task<IActionResult> Query([FromQuery] string? query, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await _query.ListAsync(query, pageIndex, pageSize, cancellationToken));

    /// <summary>按当前入口读取角色目录。</summary>
    [HttpGet]
    [RequireFeature("authorization.role.read")]
    public async Task<ActionResult<List<RoleDto>>> GetAllRoles(CancellationToken cancellationToken)
    {
        var roles = await _roleService.GetAllRolesAsync(cancellationToken);
        return Ok(roles);
    }

    /// <summary>按入口或发起动作读取角色详情。</summary>
    [HttpGet("{roleId}")]
    [RequireFeature("authorization.role.read")]
    public async Task<ActionResult<RoleDto>> GetRoleById(string roleId, CancellationToken cancellationToken)
    {
        var role = await _query.GetAsync(roleId, cancellationToken);
        if (role == null)
        {
            return NotFound();
        }
        return Ok(role);
    }

    /// <summary>创建普通角色，校验失败保持请求失败。</summary>
    [HttpPost]
    [RequireFeature("authorization.role.create")]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.CreateRoleAsync(dto, User.Identity?.Name, cancellationToken);
            return CreatedAtAction(nameof(GetRoleById), new { roleId = role.Id }, role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    /// <summary>保留旧更新协议和管理员保护，客户端不能改写角色身份。</summary>
    [HttpPut("{roleId}")]
    [RequireFeature("authorization.role.update")]
    public async Task<ActionResult<RoleDto>> UpdateRole(string roleId, [FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.UpdateRoleAsync(roleId, dto, User.Identity?.Name, cancellationToken);
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    /// <summary>保留旧协议；当前没有声明删除入口，HTTP 明确拒绝。</summary>
    [HttpDelete("{roleId}")]
    [DenyAll("Role deletion has no declared user entry position.")]
    public async Task<ActionResult> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.DeleteRoleAsync(roleId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

}
