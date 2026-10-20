using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    public sealed record OwnershipInput(WorkspacePosition? Position, string? AccountId = null);
    public sealed record OwnershipBatchInput(WorkspacePosition? Position, string[]? LinkIds);

    /// <summary>明确选择集合自动按创建账号补齐；单项资格或写入失败整体回滚，不自动把未知账号归给管理员。</summary>
    [HttpPost("ownership/initialize"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public async Task<IResult> InitializeOwnershipMany(OwnershipBatchInput input, [FromServices] EntityOwnershipBackfill backfill, CancellationToken token)
    {
        try { return Results.Ok(await contents.InitializeOwnershipManyAsync(Actor(), RequireOwnershipPosition(input.Position), input.LinkIds ?? [], backfill, token)); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }

    /// <summary>普通功能授权不能提升为存量补齐维护身份，页面可安全隐藏管理员入口。</summary>
    [HttpGet("ownership/eligibility"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public IResult OwnershipEligibility() => Results.Ok(new { allowed = true });

    /// <summary>待补齐对象独立分页列出，来源和读取范围不因管理员身份被省略。</summary>
    [HttpGet("ownership/pending"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public async Task<IResult> PendingOwnership([FromQuery] string? queryScope, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        try { return Results.Ok(await contents.PendingOwnershipAsync(Actor(), ReadOwnershipPosition(queryScope, paging: true), page, pageSize, token)); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }

    /// <summary>受控补齐先读取真实缺失状态；不以裸对象ID或调用方所称模型旁路来源。</summary>
    [HttpGet("content/{linkId}/ownership/status"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public async Task<IResult> OwnershipStatus(string linkId, [FromQuery] string? queryScope, CancellationToken token)
    {
        try
        {
            return Results.Ok(await contents.ReadOwnershipAsync(Actor(), ReadOwnershipPosition(queryScope), linkId, token));
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }

    /// <summary>缺失旧对象的归属可由管理员显式初始化；已完整对象保持不变，不成为代转入口。</summary>
    [HttpPost("content/{linkId}/ownership"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public async Task<IResult> InitializeOwnership(string linkId, OwnershipInput input, [FromServices] EntityOwnershipBackfill backfill, CancellationToken token)
    {
        try
        {
            var changed = await contents.InitializeOwnershipAsync(Actor(), RequireOwnershipPosition(input.Position), linkId, input.AccountId, backfill, token);
            return Results.Ok(new { succeeded = true, changed });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }

    // 只翻译本域正式声明错误；事务与取消保持原异常，不扩大HTTP协议。
    private static IResult OwnershipDeclarationFailure(ActionDataAccessException exception) => Results.Problem(statusCode: exception.Status, title: exception.Code,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });

    // 缺少来源不能降级为对象ID或公共根，保持与普通对象动作同一请求失败外观。
    private static WorkspacePosition RequireOwnershipPosition(WorkspacePosition? position) => position
        ?? throw new LinkQueryException(400, "Workspace.InvalidPosition");

    // 只消费完整位置及分页参数，未知字段和缺失链不能降级为裸对象或任意根。
    private WorkspacePosition ReadOwnershipPosition(string? queryScope, bool paging = false, string? additionalKey = null)
    {
        if (queryScope is null || Request.Query["queryScope"].Count != 1
            || Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key != "queryScope" && pair.Key != additionalKey
                && !(paging && pair.Key is "page" or "pageSize")))
            throw new LinkQueryException(400, "Workspace.InvalidPosition");
        return ParseOwnershipPosition(queryScope);
    }

    // 只有HTTP边界校验过查询参数的调用方可解析同一完整位置，不接受额外字段或缺失链。
    private static WorkspacePosition ParseOwnershipPosition(string queryScope)
    {
        var position = JsonSerializer.Deserialize<WorkspacePosition>(queryScope, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        if (position is null || string.IsNullOrWhiteSpace(position.SpaceId) || string.IsNullOrWhiteSpace(position.RootFolderId)
            || position.LinkIds is null || position.LinkIds.Any(string.IsNullOrWhiteSpace))
            throw new LinkQueryException(400, "Workspace.InvalidPosition");
        return position;
    }

    /// <summary>人工补齐使用最小账号候选，不开放人员档案，也不改变普通用户目录权限。</summary>
    [HttpGet("ownership/accounts"), Authorize(Policy = "AdminOnly"), RequireFeature("workspace.folder.initializeOwnership")]
    public async Task<IResult> OwnershipAccounts([FromQuery] string? query, [FromServices] IAccountOwnershipDirectory directory, CancellationToken token) =>
        Results.Ok(await directory.SearchAsync(query, token));
}
