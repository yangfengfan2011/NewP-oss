using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Audit.Controllers;

[ApiController]
[Route("api/audit/access-logs")]
public class AccessLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AccessLogsController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AccessLogDto>>> GetAccessLogs(
        [FromQuery] QueryAccessLogRequest request)
    {
        var (items, total) = await _auditService.QueryAccessLogsAsync(request);

        return Ok(new PagedResult<AccessLogDto>
        {
            Items = items,
            Total = total,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        });
    }
}
