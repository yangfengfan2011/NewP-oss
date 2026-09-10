using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.AspNetCore.Mvc;
namespace ICP.Platform.Modules.Audit.Controllers;
/// <summary>只读管理员入口；View 权限不替代当前 LocalAdmin 资格。</summary>
[ApiController, Route("api/audit/business-changes"), EndpointModule("Audit")]
[RequirePermission("Audit.BusinessChange.View")]
public sealed class BusinessChangesController(BusinessAuditQuery query, ILocalAdministratorRoleAssignment administrators) : ControllerBase
{
    /// <summary>在当前角色资格校验后读取存储侧筛选分页。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BusinessAuditFilter filter, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(cancellationToken)) return Forbid();
        if (filter.From is { Kind: not DateTimeKind.Utc } || filter.To is { Kind: not DateTimeKind.Utc }
            || filter.From > filter.To) return BadRequest(new ProblemDetails { Title = "时间范围必须为有序 UTC 时间点。", Status = 400 });
        return Ok(await query.ListAsync(filter, cancellationToken));
    }
    /// <summary>详情只依赖稳定记录 ID，不要求目标业务对象仍存在。</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(cancellationToken)) return Forbid();
        var row = await query.GetAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(new { fields = row, summaries = new { changes = new { status = "ready", data = row["Changes"] } } });
    }
    // 资格来自可信主体与实时角色分配，客户端不能选择操作者。
    private async ValueTask<bool> IsAdministratorAsync(CancellationToken token)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null && await administrators.InspectAsync(id, token) == LocalAdministratorAssignmentState.Assigned;
    }
}
