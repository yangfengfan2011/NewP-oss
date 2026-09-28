using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>所有已登记资源共用对象授权管理协议，资格由资源所属功能位置判定。</summary>
[ApiController, Route("api/authorization/data-scope/objects/{resourceKey}/{objectId}"), EndpointModule("Authorization"),
 AuthenticatedOnly, ResourceFeatureBinding("authorize")]
public sealed class DataScopeObjectsController(DataScopeObjectManagement management) : ControllerBase
{
    /// <summary>完整解释操作保留操作者身份，HTTP 层只翻译操作结果。</summary>
    [HttpGet("explanation")]
    public async Task<IActionResult> Explanation(string resourceKey, string objectId, [FromQuery] string? accountId,
        CancellationToken cancellationToken) =>
        ReadResult(await management.ExplainAsync(resourceKey, objectId, accountId, cancellationToken));

    /// <summary>按钮在发起编辑前取得真实服务端资格；响应不公开整份配置。</summary>
    [HttpGet("eligibility")]
    public async Task<IActionResult> Eligibility(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        await management.CheckAsync(resourceKey, objectId, cancellationToken) switch
        {
            DataScopeObjectEligibility.Allowed => Ok(),
            DataScopeObjectEligibility.NotFound => NotFound(),
            _ => Forbid()
        };

    /// <summary>读取完整操作自行检查目标资格；拒绝保持原有 404/403 响应。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        ReadResult(await management.GetAsync(resourceKey, objectId, cancellationToken));

    /// <summary>完整保存成功返回 204，不在 HTTP 层刷新配置或复判保存后的资格。</summary>
    [HttpPut]
    public async Task<IActionResult> Save(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken cancellationToken)
    {
        var result = await management.SaveAsync(resourceKey, objectId, input, cancellationToken);
        return result.Status == DataScopeObjectStatus.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>对象入口提供已通过目标管理资格检查的完整配置目录。</summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        ReadResult(await management.CatalogAsync(resourceKey, objectId, cancellationToken));

    /// <summary>最小主体候选与已有项名称由完整操作保护，HTTP 参数仅作输入适配。</summary>
    [HttpGet("subjects/{type}")]
    public async Task<IActionResult> Subjects(string resourceKey, string objectId, string type, [FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] string[]? currentIds = null, CancellationToken cancellationToken = default) =>
        ReadResult(await management.SubjectsAsync(resourceKey, objectId, type, query, page, currentIds ?? [], cancellationToken));

    // 成功仍使用既有 payload，不把模块操作结果包装成新的 HTTP 契约。
    private IActionResult ReadResult<T>(DataScopeObjectResult<T> result) =>
        result.Status == DataScopeObjectStatus.Succeeded ? Ok(result.Value) : Failure(result);

    // 响应只翻译已判定的拒绝与输入错误，不捕获事实源或配置故障。
    private IActionResult Failure(DataScopeObjectResult result) => result.Status switch
    {
        DataScopeObjectStatus.NotFound => NotFound(),
        DataScopeObjectStatus.Forbidden => Forbid(),
        DataScopeObjectStatus.InvalidInput when result.ValidationMessage is null => BadRequest(),
        DataScopeObjectStatus.InvalidInput => BadRequest(new ProblemDetails
        {
            Status = 400, Title = result.ValidationMessage, Extensions = { ["code"] = "DataScope.InvalidConfiguration" }
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
