using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/tree")]
[EndpointModule("Organization")]
[Authorize(Policy = OrganizationTreeAccess.PolicyName)]
public sealed class OrganizationTreeController(OrganizationTreeReader reader) : ControllerBase
{
    /// <summary>读取公司根节点。</summary>
    [HttpGet("roots")]
    public async Task<IActionResult> Roots([FromQuery] string scope, CancellationToken token)
    { if (scope != "global") return InvalidScope(); return Ok(await reader.RootsAsync(token)); }

    /// <summary>读取现存节点的全部直属节点。</summary>
    [HttpGet("children")]
    public async Task<IActionResult> Children([FromQuery] string scope, [FromQuery] string nodeKey, CancellationToken token)
    {
        if (scope != "global") return InvalidScope();
        var nodes = await reader.ChildrenAsync(nodeKey, token);
        return nodes is null ? NotFound() : Ok(nodes);
    }

    /// <summary>重新解析目标当前路径，不接受搜索时的过期位置。</summary>
    [HttpGet("path")]
    public async Task<IActionResult> Path([FromQuery] string scope, [FromQuery] string nodeKey, CancellationToken token)
    {
        if (scope != "global") return InvalidScope();
        var path = await reader.PathAsync(nodeKey, token);
        return path is null ? NotFound() : Ok(path);
    }

    /// <summary>搜索一页节点及批量祖先路径。</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string scope, [FromQuery] string keyword, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        if (scope != "global") return InvalidScope();
        if (string.IsNullOrWhiteSpace(keyword) || page < 1 || pageSize < 1 || page > int.MaxValue / pageSize)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            { ["search"] = ["搜索需要非空 keyword、正页码和正整数 pageSize。"] }));
        return Ok(await reader.SearchAsync(keyword, page, pageSize, token));
    }

    // 首个真实场景只声明全局组织范围；缺失输入不隐式回退。
    private BadRequestObjectResult InvalidScope() => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
    { ["scope"] = ["缺少或无效组织树范围。"] }));
}
