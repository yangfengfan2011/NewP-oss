using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>只读按钮拥有真实GET实现；原authorize功能和read/authorize对象要求不因只读呈现降低。</summary>
[ApiController, Route("api/authorization/data-scope/objects/{resourceKey}/{objectId}/inspection"), EndpointModule("Authorization"),
 AuthenticatedOnly, ResourceFeatureBinding("authorize"), ServiceFilter(typeof(DataExecutionExceptionFilter))]
public sealed class DataScopeObjectInspectionsController(DataScopeObjectManagement management,
    IButtonBusinessExecutor buttons, DataScopeObjectInspectionHandler inspection) : ControllerBase
{
    /// <summary>只读完整配置仍有原管理门槛，响应不成为保存来源。</summary>
    [HttpGet, ButtonBusinessHandler(typeof(DataScopeObjectInspectionHandler), "viewAuthorization")]
    public Task<IActionResult> Get(string resourceKey, string objectId, CancellationToken token) =>
        ReadAsync(new(resourceKey, objectId, "configuration"), token);

    /// <summary>读取资格不公开配置，也不能作为以后写入的允许凭证。</summary>
    [HttpGet("eligibility"), ButtonBusinessHandler(typeof(DataScopeObjectInspectionHandler), "viewAuthorization")]
    public Task<IActionResult> Eligibility(string resourceKey, string objectId, CancellationToken token) =>
        ReadAsync(new(resourceKey, objectId, "eligibility"), token);

    /// <summary>只读目录保持原完整授权要求，关闭动作不被裁掉。</summary>
    [HttpGet("catalog"), ButtonBusinessHandler(typeof(DataScopeObjectInspectionHandler), "viewAuthorization")]
    public Task<IActionResult> Catalog(string resourceKey, string objectId, CancellationToken token) =>
        ReadAsync(new(resourceKey, objectId, "catalog"), token);

    /// <summary>主体目录仍在当前对象完整授权资格下提供最小候选。</summary>
    [HttpGet("subjects/{type}"), ButtonBusinessHandler(typeof(DataScopeObjectInspectionHandler), "viewAuthorization")]
    public Task<IActionResult> Subjects(string resourceKey, string objectId, string type, [FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] string[]? currentIds = null, CancellationToken token = default) =>
        ReadAsync(new(resourceKey, objectId, "subjects", type, query, page, currentIds), token);

    /// <summary>被分析账号不替换操作者，裁决及目录故障继续传播。</summary>
    [HttpGet("explanation"), ButtonBusinessHandler(typeof(DataScopeObjectInspectionHandler), "viewAuthorization")]
    public Task<IActionResult> Explanation(string resourceKey, string objectId, [FromQuery] string? accountId, CancellationToken token) =>
        ReadAsync(new(resourceKey, objectId, "explanation", AccountId: accountId), token);

    // 功能资格仍按原资源authorize位置评估；实现绑定由真实只读handler独立证明。
    private async Task<IActionResult> ReadAsync(DataScopeObjectReadInput input, CancellationToken token)
    {
        if (await management.CheckFeatureAsync(input.ResourceKey, token) != DataScopeObjectEligibility.Allowed) return Forbid();
        var result = await buttons.ExecuteAsync(inspection, input, token);
        return result.Status switch
        {
            DataScopeAuthorizationStatus.Succeeded => input.Operation == "eligibility" ? Ok() : Ok(result.Value),
            DataScopeAuthorizationStatus.NotFound => NotFound(),
            DataScopeAuthorizationStatus.Forbidden => Forbid(),
            DataScopeAuthorizationStatus.InvalidInput when result.Error is null => BadRequest(),
            DataScopeAuthorizationStatus.InvalidInput => BadRequest(new ProblemDetails
            {
                Status = 400, Title = result.Error!.Message,
                Extensions = { ["code"] = "DataScope.InvalidConfiguration", ["reasonKey"] = result.Error.ReasonKey,
                    ["args"] = result.Error.Args, ["fieldErrors"] = result.Error.Field is null ? null : new[]
                    { new { field = result.Error.Field, code = result.Error.Code, reasonKey = result.Error.ReasonKey,
                        args = result.Error.Args, message = result.Error.Message } } }
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
