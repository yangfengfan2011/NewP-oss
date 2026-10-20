using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>所有已登记资源共用对象授权管理协议，资格由资源所属功能位置判定。</summary>
[ApiController, Route("api/authorization/data-scope/objects/{resourceKey}/{objectId}"), EndpointModule("Authorization"),
 AuthenticatedOnly, ResourceFeatureBinding("authorize"), ServiceFilter(typeof(DataExecutionExceptionFilter))]
public sealed class DataScopeObjectsController(DataScopeObjectManagement management, IButtonBusinessExecutor buttons) : ControllerBase
{
    /// <summary>完整解释保留管理资格和被分析账号，不切换操作者。</summary>
    [HttpGet("explanation"), ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public Task<IActionResult> Explanation(string resourceKey, string objectId, [FromQuery] string? accountId, CancellationToken cancellationToken) =>
        ReadAsync(new(resourceKey, objectId, "explanation", AccountId: accountId), cancellationToken);

    /// <summary>编辑前的资格探测使用正式管理按钮，不公开完整配置。</summary>
    [HttpGet("eligibility"), ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public Task<IActionResult> Eligibility(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        ReadAsync(new(resourceKey, objectId, "eligibility"), cancellationToken);

    /// <summary>配置读取仍需当前完整管理资格，不借资源owner的纯查看入口。</summary>
    [HttpGet, ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public Task<IActionResult> Get(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        ReadAsync(new(resourceKey, objectId, "configuration"), cancellationToken);

    /// <summary>完整保存成功返回204；功能前置和原锁内对象重验保持，不追加成功后回读。</summary>
    [HttpPut, ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public async Task<IActionResult> Save(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken cancellationToken)
    {
        if (await management.CheckFeatureAsync(resourceKey, cancellationToken) != DataScopeObjectEligibility.Allowed) return Forbid();
        var result = await buttons.ExecuteAsync((IDataScopeObjectSaveHandler)management, new DataScopeObjectSaveInput(resourceKey, objectId, input), cancellationToken);
        return result.Status == DataScopeAuthorizationStatus.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>通用目录保持原管理门槛；纯read目录仍由资源owner拥有。</summary>
    [HttpGet("catalog"), ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public Task<IActionResult> Catalog(string resourceKey, string objectId, CancellationToken cancellationToken) =>
        ReadAsync(new(resourceKey, objectId, "catalog"), cancellationToken);

    /// <summary>通用主体目录保留完整管理资格与原分页/当前值协议。</summary>
    [HttpGet("subjects/{type}"), ButtonBusinessHandler(typeof(DataScopeObjectManagement))]
    public Task<IActionResult> Subjects(string resourceKey, string objectId, string type, [FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] string[]? currentIds = null, CancellationToken cancellationToken = default) =>
        ReadAsync(new(resourceKey, objectId, "subjects", type, query, page, currentIds), cancellationToken);

    // ResourceFeatureBinding提供动态资源身份；原generic功能评估仍在业务处理器前执行。
    private async Task<IActionResult> ReadAsync(DataScopeObjectReadInput input, CancellationToken token)
    {
        if (await management.CheckFeatureAsync(input.ResourceKey, token) != DataScopeObjectEligibility.Allowed) return Forbid();
        var result = await buttons.ExecuteAsync((IDataScopeObjectReadHandler)management, input, token);
        return result.Status == DataScopeAuthorizationStatus.Succeeded
            ? input.Operation == "eligibility" ? Ok() : Ok(result.Value) : Failure(result);
    }

    // 只翻译既有资格和配置错误事实，依赖故障及取消继续由原边界处理。
    private IActionResult Failure(DataScopeAuthorizationResult result) => result.Status switch
    {
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
