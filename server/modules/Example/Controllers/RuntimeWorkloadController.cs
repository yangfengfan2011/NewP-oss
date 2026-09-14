using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Example.DTOs;
using ICP.Platform.Modules.Example.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Example.Controllers;

[ApiController]
[Route("api/example/runtime-workload")]
[EndpointModule("Example")]
[RequireFeature("example.runtime-workload.read")]
public sealed class RuntimeWorkloadController : ControllerBase
{
    private const string ColumnContextHeader = "X-Example-Column-Context";
    private readonly IRuntimeWorkloadService _service;

    // Controller 只依赖模块应用服务，不把日期列计算带入 HTTP 边界。
    public RuntimeWorkloadController(IRuntimeWorkloadService service)
    {
        _service = service;
    }

    // Controller 只暴露模块 API，日期列计算和 token 解释均由模块服务负责。
    [HttpGet("columns")]
    public ActionResult<RuntimeWorkloadColumnSetDto> GetColumns() => Ok(_service.GetColumns());

    // 行 API 保持标准分页查询形状，并从模块私有 header 把同一列代际 token 交给模块服务。
    [HttpGet("rows")]
    public ActionResult<RuntimeWorkloadListResultDto> GetRows(
        [FromHeader(Name = ColumnContextHeader)] string contextToken,
        [FromQuery] string? keyword = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null) =>
        Ok(_service.GetRows(contextToken, keyword, pageIndex, pageSize, sortBy, sortDirection));
}
