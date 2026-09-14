using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;
using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[Route("api/authorization/users")]
[EndpointModule("Authorization")]
public class UserRoleController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly IIdentityAccountDirectory _accounts;

    // 账号由 Identity 稳定目录提供，控制器只翻译确定性输入错误。
    public UserRoleController(IRoleService roleService, IIdentityAccountDirectory accounts)
    {
        _roleService = roleService;
        _accounts = accounts;
    }

    /// <summary>按关键词搜索有界账号候选，停用账号仍可引用。</summary>
    [HttpGet("candidates")]
    [RequireFeature("authorization.userRoles.read")]
    public async Task<IActionResult> Candidates([FromQuery] string? query, CancellationToken cancellationToken) =>
        Ok((await _accounts.SearchAsync(new IdentityAccountSearch { Query = query }, cancellationToken))
            .Where(a => !a.IsLocalAdministrator && a.Status != IdentityAccountStatus.Deleted)
            .Select(a => new { id = a.AccountEid, code = a.ECODE, name = a.ENAME, status = a.Status.ToString() }));

    /// <summary>查询普通账号配置，受保护账号返回明确失败。</summary>
    [HttpGet("{userId}/roles")]
    [RequireFeature("authorization.userRoles.read")]
    public async Task<ActionResult<UserRoleAssignmentsDto>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        try { return Ok(await _roleService.GetUserRolesAsync(userId, cancellationToken)); }
        catch (UserRoleValidationException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
    }

    /// <summary>完整替换普通角色，登记及提交异常继续传播。</summary>
    [HttpPut("{userId}/roles")]
    [RequireFeature("authorization.role.userRoles")]
    public async Task<ActionResult> AssignUserRoles(string userId, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.AssignUserRolesAsync(userId, request, User.Identity?.Name, cancellationToken);
            return NoContent();
        }
        catch (UserRoleValidationException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
    }

}
