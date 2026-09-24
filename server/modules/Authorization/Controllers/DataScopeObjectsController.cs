using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>所有已登记资源共用对象授权管理协议，资格由资源所属功能位置判定。</summary>
[ApiController, Route("api/authorization/data-scope/objects/{resourceKey}/{objectId}"), EndpointModule("Authorization"),
 AuthenticatedOnly, ResourceFeatureBinding("authorize")]
public sealed class DataScopeObjectsController(DataScopeObjectManagement management, DataScopeCatalog catalog,
    DataScopeIdentities identities) : ControllerBase
{
    /// <summary>只允许具备对象管理资格的操作者分析用户；被分析用户不替换请求的可信身份。</summary>
    [HttpGet("explanation")]
    public async Task<IActionResult> Explanation(string resourceKey, string objectId, [FromQuery] string? accountId,
        CancellationToken cancellationToken)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        if (eligibility != DataScopeObjectEligibility.Allowed) return Failure(eligibility);
        var target = accountId ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(target) || target.Length > 128) return BadRequest();
        var result = await management.ExplainAsync(resourceKey, objectId, target, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>按钮在发起编辑前取得真实服务端资格；响应不公开整份配置。</summary>
    [HttpGet("eligibility")]
    public async Task<IActionResult> Eligibility(string resourceKey, string objectId, CancellationToken cancellationToken)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        return eligibility == DataScopeObjectEligibility.Allowed ? Ok() : Failure(eligibility);
    }

    /// <summary>目标读取或对象动作拒绝在调用配置读取前分别返回 404/403。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(string resourceKey, string objectId, CancellationToken cancellationToken)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        if (eligibility != DataScopeObjectEligibility.Allowed) return Failure(eligibility);
        return Ok(await management.GetAsync(resourceKey, objectId, cancellationToken));
    }

    /// <summary>完整保存按请求开始时的资格判定，成功后不再读取配置或复判。</summary>
    [HttpPut]
    public async Task<IActionResult> Save(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken cancellationToken)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        if (eligibility != DataScopeObjectEligibility.Allowed) return Failure(eligibility);
        try
        {
            if (input.ResourceKey != resourceKey || input.ObjectId != objectId)
                throw new RolePermissionValidationException("对象授权目标与请求不一致。");
            await management.SaveAsync(input, cancellationToken);
            return NoContent();
        }
        catch (RolePermissionValidationException exception)
        { return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message, Extensions = { ["code"] = "DataScope.InvalidConfiguration" } }); }
    }

    /// <summary>对象入口提供完整动作及主体类型目录，关闭动作仍在 authoring 中可配置。</summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(string resourceKey, string objectId, CancellationToken cancellationToken)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        if (eligibility != DataScopeObjectEligibility.Allowed) return Failure(eligibility);
        return Ok(new { actions = catalog.Actions, subjectTypes = identities.SubjectTypes });
    }

    /// <summary>最小主体候选与已有项名称均受目标对象管理资格约束。</summary>
    [HttpGet("subjects/{type}")]
    public async Task<IActionResult> Subjects(string resourceKey, string objectId, string type, [FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] string[]? currentIds = null, CancellationToken cancellationToken = default)
    {
        var eligibility = await management.CheckAsync(resourceKey, objectId, cancellationToken);
        if (eligibility != DataScopeObjectEligibility.Allowed) return Failure(eligibility);
        try { return Ok(await management.SubjectsAsync(type, query, page, currentIds ?? [], cancellationToken)); }
        catch (RolePermissionValidationException exception)
        { return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message, Extensions = { ["code"] = "DataScope.InvalidConfiguration" } }); }
    }

    // 响应仅翻译已判定的拒绝，不吞掉事实源或配置异常。
    private IActionResult Failure(DataScopeObjectEligibility eligibility) =>
        eligibility == DataScopeObjectEligibility.NotFound ? NotFound() : Forbid();
}
