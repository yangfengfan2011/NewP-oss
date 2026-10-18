using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    /// <summary>点击进入前只验证当前声明与完整出现位置；成功不产生后续请求凭据。</summary>
    [HttpGet("content/{linkId}/history/check/{operation}"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> CheckHistory(string linkId, string operation, [FromQuery] string? queryScope,
        [FromServices] IActionDataAccess access, CancellationToken token)
    {
        try
        {
            if (operation is not ("enter" or "preview" or "detail")) return Results.BadRequest();
            await ResolveDeclaredHistoryTargetAsync(linkId, queryScope, "workspace.history." + operation, access, token);
            return Results.NoContent();
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>普通履历每次重解完整来源与真实EID；原功能资格与按钮声明都不能替代owner完整读取。</summary>
    [HttpGet("content/{linkId}/history"), RequireFeature("workspace.history.read")]
    public async Task<IResult> History(string linkId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] IObjectHistoryQuery history, [FromServices] IActionDataAccess access, CancellationToken token)
    {
        try
        {
            var target = await ResolveDeclaredHistoryTargetAsync(linkId, queryScope, "workspace.history.list", access, token);
            var page = await history.ListAsync(target.ResourceKey, target.ObjectId, filter, token);
            return page is null ? Results.NotFound() : Results.Ok(page);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历筛选无效。", Status = 400 }); }
    }

    /// <summary>详情仍按当前来源与对象安全集合精确取记录；不能使用管理员按裸审计ID查询的出口。</summary>
    [HttpGet("content/{linkId}/history/{recordId}"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> HistoryDetail(string linkId, string recordId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] IObjectHistoryQuery history, [FromServices] IActionDataAccess access, CancellationToken token)
    {
        try
        {
            var target = await ResolveDeclaredHistoryTargetAsync(linkId, queryScope, "workspace.history.detail", access, token);
            var item = await history.GetAsync(target.ResourceKey, target.ObjectId, recordId, filter, token);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历筛选无效。", Status = 400 }); }
    }

    /// <summary>导出保留原功能资格，并按自身正式声明和完整来源读取安全集合。</summary>
    [HttpPost("content/{linkId}/history/export"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> ExportHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] IObjectHistoryExport export, [FromServices] IActionDataAccess access, CancellationToken token)
    {
        try
        {
            var target = await ResolveDeclaredHistoryTargetAsync(linkId, queryScope, "workspace.history.export", access, token);
            var file = await export.ExportAsync(target.ResourceKey, target.ObjectId, request, token);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历输出筛选或范围无效。", Status = 400 }); }
    }

    /// <summary>打印只返回与页面相同的安全集合；实际打印由受控前端视图触发，不返回任意HTML。</summary>
    [HttpPost("content/{linkId}/history/print"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> PrintHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] IObjectHistoryQuery history, [FromServices] IActionDataAccess access, CancellationToken token)
    {
        try
        {
            var target = await ResolveDeclaredHistoryTargetAsync(linkId, queryScope, "workspace.history.print", access, token);
            var items = await history.OutputAsync(target.ResourceKey, target.ObjectId, request, token);
            return items is null ? Results.NotFound() : Results.Ok(new { items });
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历输出筛选或范围无效。", Status = 400 }); }
    }

    // 声明只确定数据动作；来源owner每次以完整位置和LID重解真实资源，所有出口都重新检查。
    private async Task<(string ResourceKey, string ObjectId)> ResolveDeclaredHistoryTargetAsync(string linkId, string? queryScope,
        string execution, IActionDataAccess access, CancellationToken token)
    {
        var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
        if (!await access.IsAllowedAsync(execution, target.ResourceKey, target.ObjectId, token))
            throw new LinkQueryException(404, "ObjectHistory.Unavailable");
        return target;
    }

    // 履历筛选独立于来源JSON；只允许当前正式过滤字段，不能借分页参数放宽位置或接受重复输入。
    private WorkspacePosition ReadHistoryPosition(string? queryScope)
    {
        if (queryScope is null || Request.Query["queryScope"].Count != 1
            || Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key is not
                ("queryScope" or "pageIndex" or "pageSize" or "from" or "to" or "actorId" or "eventType" or "objectType" or "ipAddress" or "description")))
            throw new LinkQueryException(400, "Workspace.InvalidPosition");
        return ParseOwnershipPosition(queryScope);
    }

}
