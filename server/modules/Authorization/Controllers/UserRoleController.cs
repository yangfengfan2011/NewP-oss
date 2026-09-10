using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;
using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Authorization.Controllers;

[ApiController]
[ServiceFilter(typeof(LocalAdministratorManagementFilter))]
[Route("api/authorization/users")]
[EndpointModule("Authorization")]
public class UserRoleController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IIdentityAccountDirectory _accounts;

    // 账号由 Identity 稳定目录提供，控制器只翻译确定性输入错误。
    public UserRoleController(IRoleService roleService, IAuthorizationService authorizationService, IIdentityAccountDirectory accounts)
    {
        _roleService = roleService;
        _authorizationService = authorizationService;
        _accounts = accounts;
    }

    /// <summary>按关键词搜索有界账号候选，停用账号仍可引用。</summary>
    [HttpGet("candidates")]
    [RequirePermission(AuthorizationPermissions.RoleView)]
    public async Task<IActionResult> Candidates([FromQuery] string? query, CancellationToken cancellationToken) =>
        Ok((await _accounts.SearchAsync(new IdentityAccountSearch { Query = query }, cancellationToken))
            .Where(a => !a.IsLocalAdministrator && a.Status != IdentityAccountStatus.Deleted)
            .Select(a => new { id = a.AccountEid, code = a.ECODE, name = a.ENAME, status = a.Status.ToString() }));

    /// <summary>查询普通账号配置，受保护账号返回明确失败。</summary>
    [HttpGet("{userId}/roles")]
    [RequirePermission(AuthorizationPermissions.RoleView)]
    public async Task<ActionResult<UserRoleAssignmentsDto>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        try { return Ok(await _roleService.GetUserRolesAsync(userId, cancellationToken)); }
        catch (UserRoleValidationException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
    }

    /// <summary>完整替换普通角色，登记及提交异常继续传播。</summary>
    [HttpPut("{userId}/roles")]
    [RequirePermission(AuthorizationPermissions.UserAssignRole)]
    public async Task<ActionResult> AssignUserRoles(string userId, [FromBody] AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _roleService.AssignUserRolesAsync(userId, request, User.Identity?.Name, cancellationToken);
            return NoContent();
        }
        catch (UserRoleValidationException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
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
