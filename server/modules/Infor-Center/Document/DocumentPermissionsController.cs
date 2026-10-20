using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>独立对象入口沿本owner功能资格，查询和直接项管理分别复用公共服务。</summary>
[ApiController, Route("api/document/documents/{objectId}/permissions"), EndpointModule("Document")]
public sealed class DocumentPermissionsController(IDataScopeObjectPermissionQueries queries, IDataScopeObjectAuthorization authorization) : ControllerBase
{
    private const string ResourceKey = "document.document";

    /// <summary>对象可浏览即可查询目录，不公开直接配置。</summary>
    [HttpGet("catalog"), RequireFeature("document.document.read")]
    public async Task<IResult> Catalog(string objectId, CancellationToken token) => await WithDataAccessAsync(async () =>
    {
        var result = await queries.CatalogAsync(ResourceKey, objectId, ViewSources, token);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    /// <summary>被分析账号只改变解释目标，不替换当前请求身份。</summary>
    [HttpGet("explanation"), RequireFeature("document.document.read")]
    public async Task<IResult> Explanation(string objectId, [FromQuery] string? accountId, CancellationToken token)
    {
        if (accountId is not null && (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 128))
            return Failure(DataScopeAuthorizationStatus.InvalidInput, new("被查询账号无效。", "DATA_SCOPE_ACCOUNT_INVALID",
                "AuthorizationErrors.DATA_SCOPE_ACCOUNT_INVALID", new Dictionary<string, object?>(), "accountId"));
        return await WithDataAccessAsync(async () =>
        {
            var result = await queries.InspectAsync(ResourceKey, objectId, accountId, ViewSources, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    /// <summary>只读账号选择仅开放最小用户候选。</summary>
    [HttpGet("subjects/user"), RequireFeature("document.document.read")]
    public async Task<IResult> Users(string objectId, [FromQuery] string? query, [FromQuery] int? page,
        [FromQuery] string[]? currentIds, CancellationToken token) =>
        await WithDataAccessAsync(async () => Read(await queries.UsersAsync(ResourceKey, objectId, query, page ?? 1, currentIds ?? [], ViewSources, token)));

    /// <summary>其他授权主体候选仍需完整对象管理资格。</summary>
    [HttpGet("subjects/{type}"), RequireFeature("document.document.authorize")]
    public async Task<IResult> Subjects(string objectId, string type, [FromQuery] string? query, [FromQuery] int? page,
        [FromQuery] string[]? currentIds, CancellationToken token) =>
        await WithDataAccessAsync(async () => Read(await authorization.SubjectsAsync(ResourceKey, objectId, type, query, page ?? 1, currentIds ?? [], Management("prepare"), token)));

    /// <summary>管理资格与业务动作分离，每次检查当前read及data-authorize。</summary>
    [HttpGet("eligibility"), RequireFeature("document.document.authorize")]
    public async Task<IResult> Eligibility(string objectId, CancellationToken token) =>
        await WithDataAccessAsync(async () => Failure(await authorization.CheckAsync(ResourceKey, objectId, Management("prepare"), token)));

    /// <summary>配置只对当前仍有完整管理资格的调用者公开。</summary>
    [HttpGet, RequireFeature("document.document.authorize")]
    public async Task<IResult> Configuration(string objectId, CancellationToken token) =>
        await WithDataAccessAsync(async () => Read(await authorization.ReadAsync(ResourceKey, objectId, Management("prepare"), token)));

    /// <summary>完整替换由同一事务提交；成功后不回读配置，保留自身失权的安全成功。</summary>
    [HttpPut, RequireFeature("document.document.authorize")]
    public async Task<IResult> Replace(string objectId, DataScopeObjectConfiguration input, CancellationToken token)
        => await WithDataAccessAsync(async () =>
    {
        var result = await authorization.ReplaceAsync(ResourceKey, objectId, input, Management("submit"), token);
        return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.NoContent() : Failure(result.Status, result.Error);
    });

    // 原owner入口固定执行和阶段；客户端只能引用自己的正式动作声明，不能选择另一个管理能力。
    private static DataExecutionBindingReference Management(string stage) =>
        new("document.document.authorization.manage", "authorization.object.manage", stage);

    // 同一原read端点接受实际查看来源及管理输入的辅助read来源，不能把view借到管理入口。
    private static readonly IReadOnlyList<DataExecutionBindingReference> ViewSources = [
        new("document.document.authorization.view", "authorization.object.view", "prepare"),
        new("document.document.authorization.manage", "authorization.object.manage", "prepare")];

    // owner只翻译稳定声明异常，不引用Authorization实现或拦截原事务、取消和事实源故障。
    private static async Task<IResult> WithDataAccessAsync(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (ActionDataAccessException error)
        {
            return Results.Problem(statusCode: error.Status, title: error.Code,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }
    }

    // 仅翻译稳定操作结果；事实源故障、提交错误和取消继续传播。
    private static IResult Read<T>(DataScopeAuthorizationResult<T> result) =>
        result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value) : Failure(result.Status, result.Error);

    // 沿既有对象管理错误语义，不在owner复制授权引擎。
    private static IResult Failure(DataScopeAuthorizationStatus status, DataScopeConfigurationError? error = null) => status switch
    {
        DataScopeAuthorizationStatus.Succeeded => Results.Ok(),
        DataScopeAuthorizationStatus.NotFound => Results.NotFound(),
        DataScopeAuthorizationStatus.Forbidden => Results.Forbid(),
        DataScopeAuthorizationStatus.InvalidInput when error is null => Results.BadRequest(),
        DataScopeAuthorizationStatus.InvalidInput => Results.Problem(statusCode: 400, title: error!.Message,
            extensions: new Dictionary<string, object?> { ["code"] = "DataScope.InvalidConfiguration", ["reasonKey"] = error.ReasonKey,
                ["args"] = error.Args, ["fieldErrors"] = error.Field is null ? null : new[] { new { field = error.Field, code = error.Code,
                    reasonKey = error.ReasonKey, args = error.Args, message = error.Message } } }),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
