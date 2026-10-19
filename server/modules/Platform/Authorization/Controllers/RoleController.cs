using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
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
    [StandardCrudDataExecution("authorization.role", "authorization.role.list", "list")]
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
    [StandardCrudDataExecution("authorization.role", "authorization.role.read", "read")]
    public async Task<ActionResult<RoleDto>> GetRoleById(string roleId, CancellationToken cancellationToken)
    {
        var role = await _query.GetAsync(roleId, cancellationToken);
        if (role == null)
        {
            return NotFound();
        }
        return Ok(role);
    }

    /// <summary>创建普通角色，只翻译确定性业务拒绝，依赖异常继续传播。</summary>
    [HttpPost]
    [RequireFeature("authorization.role.create")]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.CreateRoleAsync(dto, User.Identity?.Name, cancellationToken);
            return CreatedAtAction(nameof(GetRoleById), new { roleId = role.Id }, role);
        }
        catch (RoleValidationException ex)
        {
            return BadRequest(AuthorizationProblem.From(ex));
        }
    }

    /// <summary>保留旧更新协议和管理员保护，只将确定性业务拒绝翻译为请求错误。</summary>
    [HttpPut("{roleId}")]
    [RequireFeature("authorization.role.update")]
    public async Task<ActionResult<RoleDto>> UpdateRole(string roleId, [FromBody] RoleDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var role = await _roleService.UpdateRoleAsync(roleId, dto, User.Identity?.Name, cancellationToken);
            return Ok(role);
        }
        catch (RoleValidationException ex)
        {
            return BadRequest(AuthorizationProblem.From(ex));
        }
    }

    /// <summary>保留旧协议及业务拒绝翻译；当前没有声明删除入口，HTTP 仍明确拒绝。</summary>
    [HttpDelete("{roleId}")]
    [DenyAll("Role deletion has no declared user entry position.")]
    public async Task<ActionResult> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.DeleteRoleAsync(roleId, cancellationToken);
            return NoContent();
        }
        catch (RoleValidationException ex)
        {
            return BadRequest(AuthorizationProblem.From(ex));
        }
    }

}
