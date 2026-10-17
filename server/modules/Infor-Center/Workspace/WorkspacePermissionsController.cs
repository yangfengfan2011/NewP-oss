using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    /// <summary>只读属性不借独立对象功能入口；完整出现位置和当前对象read每次重新验证。</summary>
    [HttpGet("content/{linkId}/properties"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> ContentProperties(string linkId, [FromQuery] string? queryScope, CancellationToken token)
    {
        try
        {
            var result = await contents.ReadPropertiesAsync(Actor(), ReadOwnershipPosition(queryScope), linkId, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>新查询只由真实owner出口开放；旧模块没有该路由，不改变既有通用管理合同。</summary>
    [HttpGet("content/{linkId}/permissions/explanation"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> Permissions(string linkId, [FromQuery] string? queryScope, [FromQuery] string? accountId,
        [FromServices] IDataScopeObjectPermissionQueries queries, CancellationToken token)
    {
        try
        {
            if (accountId is not null && (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 128))
                return PermissionFailure(DataScopeAuthorizationStatus.InvalidInput, new("被查询账号无效。", "DATA_SCOPE_ACCOUNT_INVALID",
                    "AuthorizationErrors.DATA_SCOPE_ACCOUNT_INVALID", new Dictionary<string, object?>(), "accountId"));
            var result = await contents.ReadPermissionsAsync(Actor(), ReadOwnershipPosition(queryScope, additionalKey: "accountId"),
                linkId, accountId, queries, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }
    /// <summary>只读目录只需当前真实对象可浏览，不读取直接项配置或给出管理资格。</summary>
    [HttpGet("content/{linkId}/permissions/catalog"), RequireFeature("workspace.folder.read")]
    public Task<IResult> PermissionCatalog(string linkId, [FromQuery] string? queryScope,
        [FromServices] IDataScopeObjectPermissionQueries queries, CancellationToken token) =>
        WithPermissionTarget(linkId, queryScope, async target =>
        {
            var result = await queries.CatalogAsync(target.ResourceKey, target.ObjectId, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }, token);

    /// <summary>分析账号选择仅开放用户最小目录，所有来源与对象读取范围每次重新核对。</summary>
    [HttpGet("content/{linkId}/permissions/subjects/user"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> PermissionUsers(string linkId, [FromQuery] string? queryScope,
        [FromQuery] string? query, [FromQuery] int? page, [FromQuery] string[]? currentIds,
        [FromServices] IDataScopeObjectPermissionQueries queries, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadPermissionSubjectsPosition(queryScope), linkId, token);
            var result = await queries.UsersAsync(target.ResourceKey, target.ObjectId, query, page ?? 1, currentIds ?? [], token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value) : PermissionFailure(result.Status, result.Error);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>其他最小主体候选只供当前对象的授权管理使用，不因查询解释而扩大候选目录。</summary>
    [HttpGet("content/{linkId}/permissions/subjects/{type}"), RequireFeature("workspace.folder.authorize")]
    public async Task<IResult> PermissionSubjects(string linkId, string type, [FromQuery] string? queryScope,
        [FromQuery] string? query, [FromQuery] int? page, [FromQuery] string[]? currentIds,
        [FromServices] IDataScopeObjectAuthorization authorization, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadPermissionSubjectsPosition(queryScope), linkId, token);
            var result = await authorization.SubjectsAsync(target.ResourceKey, target.ObjectId, type, query, page ?? 1, currentIds ?? [], token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value) : PermissionFailure(result.Status, result.Error);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 只允许目录的既有分页/搜索与当前值输入；重复currentIds仍按owner已有分块解析，不设置业务数量上限。
    private WorkspacePosition ReadPermissionSubjectsPosition(string? queryScope)
    {
        if (queryScope is null || Request.Query["queryScope"].Count != 1
            || Request.Query.Any(pair => pair.Key is not ("queryScope" or "query" or "page" or "currentIds")
                || pair.Key != "currentIds" && pair.Value.Count != 1))
            throw new LinkQueryException(400, "Workspace.InvalidPosition");
        return ParseOwnershipPosition(queryScope);
    }

    /// <summary>工作空间操作功能与实际对象data-authorize分别检查，不能借功能头切换资源owner。</summary>
    [HttpGet("content/{linkId}/permissions/eligibility"), RequireFeature("workspace.folder.authorize")]
    public Task<IResult> PermissionEligibility(string linkId, [FromQuery] string? queryScope,
        [FromServices] IDataScopeObjectAuthorization authorization, CancellationToken token) =>
        WithPermissionTarget(linkId, queryScope, async target => PermissionFailure(await authorization.CheckAsync(target.ResourceKey, target.ObjectId, token)), token);

    /// <summary>配置读取先重验完整真实位置，再交同一对象管理服务。</summary>
    [HttpGet("content/{linkId}/permissions"), RequireFeature("workspace.folder.authorize")]
    public Task<IResult> PermissionConfiguration(string linkId, [FromQuery] string? queryScope,
        [FromServices] IDataScopeObjectAuthorization authorization, CancellationToken token) =>
        WithPermissionTarget(linkId, queryScope, async target =>
        {
            var result = await authorization.ReadAsync(target.ResourceKey, target.ObjectId, token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value) : PermissionFailure(result.Status, result.Error);
        }, token);

    /// <summary>完整配置目标由当前LID解析决定；保存成功不回读，避免自身失权后的泄漏或重放。</summary>
    [HttpPut("content/{linkId}/permissions"), RequireFeature("workspace.folder.authorize")]
    public async Task<IResult> ReplacePermissions(string linkId, [FromQuery] string? queryScope, DataScopeObjectConfiguration input,
        [FromServices] IDataScopeObjectAuthorization authorization, CancellationToken token)
    {
        try
        {
            var result = await contents.ReplacePermissionsAsync(Actor(), ReadOwnershipPosition(queryScope), linkId, input, authorization, token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.NoContent() : PermissionFailure(result.Status, result.Error);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 请求格式与来源失败沿Workspace既有Problem语义；不捕获真实授权、持久化或取消故障。
    private async Task<IResult> WithPermissionTarget(string linkId, string? queryScope,
        Func<(string ResourceKey, string ObjectId), Task<IResult>> action, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadOwnershipPosition(queryScope), linkId, token);
            return await action(target);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 公共契约复用原DataScope错误事实；不把私有Authorization服务或异常引入Workspace。
    private static IResult PermissionFailure(DataScopeAuthorizationStatus status, DataScopeConfigurationError? error = null) => status switch
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
