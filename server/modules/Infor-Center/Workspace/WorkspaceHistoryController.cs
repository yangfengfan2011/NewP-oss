using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    /// <summary>普通履历每次重解完整来源与真实EID；独立功能不能代替对象read/history及owner范围。</summary>
    [HttpGet("content/{linkId}/history"), RequireFeature("workspace.history.read")]
    public async Task<IResult> History(string linkId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] IObjectHistoryQuery history, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
            var page = await history.ListAsync(target.ResourceKey, target.ObjectId, filter, token);
            return page is null ? Results.NotFound() : Results.Ok(page);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历筛选无效。", Status = 400 }); }
    }

    /// <summary>详情仍按当前来源与对象安全集合精确取记录；不能使用管理员按裸审计ID查询的出口。</summary>
    [HttpGet("content/{linkId}/history/{recordId}"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> HistoryDetail(string linkId, string recordId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] IObjectHistoryQuery history, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
            var item = await history.GetAsync(target.ResourceKey, target.ObjectId, recordId, filter, token);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历筛选无效。", Status = 400 }); }
    }

    /// <summary>导出沿同一history资格和完整来源，不委派管理员查询或普通CRUD导出。</summary>
    [HttpPost("content/{linkId}/history/export"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> ExportHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] IObjectHistoryExport export, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
            var file = await export.ExportAsync(target.ResourceKey, target.ObjectId, request, token);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历输出筛选或范围无效。", Status = 400 }); }
    }

    /// <summary>打印只返回与页面相同的安全集合；实际打印由受控前端视图触发，不返回任意HTML。</summary>
    [HttpPost("content/{linkId}/history/print"), RequireFeature("workspace.folder.history")]
    public async Task<IResult> PrintHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] IObjectHistoryQuery history, CancellationToken token)
    {
        try
        {
            var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
            var items = await history.OutputAsync(target.ResourceKey, target.ObjectId, request, token);
            return items is null ? Results.NotFound() : Results.Ok(new { items });
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历输出筛选或范围无效。", Status = 400 }); }
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
