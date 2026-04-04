using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Audit.Controllers;

[ApiController]
[Route("api/audit/operation-logs")]
public class OperationLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public OperationLogsController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OperationLogDto>>> GetOperationLogs(
        [FromQuery] QueryOperationLogRequest request)
    {
        var (items, total) = await _auditService.QueryOperationLogsAsync(request);

        return Ok(new PagedResult<OperationLogDto>
        {
            Items = items,
            Total = total,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        });
    }

    [HttpPost]
    public async Task<ActionResult<long>> CreateOperationLog([FromBody] CreateOperationLogRequest request)
    {
        var userId = User.Identity?.Name;
        var userName = User.Identity?.Name;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var id = await _auditService.LogOperationAsync(request, userId, userName, clientIp);
        return Ok(id);
    }
}
