using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Persistence.Transactions;
using Microsoft.AspNetCore.Mvc;
namespace ICP.Platform.Modules.Audit.Controllers;
/// <summary>只读管理员入口；位置读取授权不替代当前 LocalAdmin 资格。</summary>
[ApiController, Route("api/audit/business-changes"), EndpointModule("Audit")]
[RequireFeature("audit.change.read")]
public sealed class BusinessChangesController(BusinessAuditQuery query, ILocalAdministratorRoleAssignment administrators) : ControllerBase
{
    /// <summary>在当前角色资格校验后读取存储侧筛选分页。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BusinessAuditFilter filter, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(cancellationToken)) return Forbid();
        if (filter.From is { Kind: not DateTimeKind.Utc } || filter.To is { Kind: not DateTimeKind.Utc }
            || filter.From > filter.To) return BadRequest(new ProblemDetails { Title = "时间范围必须为有序 UTC 时间点。", Status = 400 });
        try { return Ok(await query.ListAsync(filter, cancellationToken)); }
        catch (PlatformCacheUnavailableException exception) { return CacheUnavailable(exception); }
    }
    /// <summary>详情只依赖稳定记录 ID，不要求目标业务对象仍存在。</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(cancellationToken)) return Forbid();
        try
        {
            var row = await query.GetAsync(id, cancellationToken);
            return row is null ? NotFound() : Ok(new { fields = row, summaries = new { changes = new { status = "ready", data = row["Changes"] } } });
        }
        catch (PlatformCacheUnavailableException exception) { return CacheUnavailable(exception); }
    }
    // 关键缓存故障返回稳定暂不可用，不改写历史事实或重放读取依赖。
    private ObjectResult CacheUnavailable(PlatformCacheUnavailableException exception)
    {
        const string code = "Persistence.CacheUnavailable";
        var problem = new ProblemDetails { Status = 503, Title = code, Detail = exception.Message };
        problem.Extensions["code"] = code;
        problem.Extensions["message"] = exception.Message;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = 503, ContentTypes = { "application/problem+json" } };
    }
    // 资格来自可信主体与实时角色分配，客户端不能选择操作者。
    private async ValueTask<bool> IsAdministratorAsync(CancellationToken token)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null && await administrators.InspectAsync(id, token) == LocalAdministratorAssignmentState.Assigned;
    }
}
