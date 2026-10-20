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
    /// <summary>点击进入前核对本次按钮及完整来源；成功不产生后续请求凭据。</summary>
    [HttpGet("content/{linkId}/history/check/{operation}"), RequireFeature("workspace.folder.history"), ButtonBusinessHandler(typeof(WorkspaceHistoryButtonHandler))]
    public Task<IResult> CheckHistory(string linkId, string operation, [FromQuery] string? queryScope,
        [FromServices] WorkspaceHistoryButtonHandler handler, CancellationToken token) =>
        operation is "enter" or "preview" or "detail"
            ? HistoryButtonAsync(linkId, queryScope, operation, new(), handler, token)
            : Task.FromResult<IResult>(Results.BadRequest());

    /// <summary>普通履历每次重解完整来源与真实EID；原功能资格与按钮声明都不能替代owner完整读取。</summary>
    [HttpGet("content/{linkId}/history"), RequireFeature("workspace.history.read")]
    public async Task<IResult> History(string linkId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] IObjectHistoryQuery history, [FromServices] IDataExecutionAccess access, CancellationToken token)
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

    /// <summary>详情按当前来源与对象安全集合精确取记录；实际按钮仍独立判权。</summary>
    [HttpGet("content/{linkId}/history/{recordId}"), RequireFeature("workspace.folder.history"), ButtonBusinessHandler(typeof(WorkspaceHistoryButtonHandler))]
    public Task<IResult> HistoryDetail(string linkId, string recordId, [FromQuery] string? queryScope,
        [FromQuery] ObjectHistoryFilter filter, [FromServices] WorkspaceHistoryButtonHandler handler, CancellationToken token) =>
        HistoryButtonAsync(linkId, queryScope, "detail", filter, handler, token, recordId);

    /// <summary>导出按自身按钮与完整来源读取安全集合，保留原筛选和数量上限。</summary>
    [HttpPost("content/{linkId}/history/export"), RequireFeature("workspace.folder.history"), ButtonBusinessHandler(typeof(WorkspaceHistoryButtonHandler))]
    public Task<IResult> ExportHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] WorkspaceHistoryButtonHandler handler, CancellationToken token) =>
        HistoryButtonAsync(linkId, queryScope, "export", request.Filter, handler, token, output: request);

    /// <summary>打印只返回原安全集合；浏览器受控视图负责实际print调用。</summary>
    [HttpPost("content/{linkId}/history/print"), RequireFeature("workspace.folder.history"), ButtonBusinessHandler(typeof(WorkspaceHistoryButtonHandler))]
    public Task<IResult> PrintHistory(string linkId, [FromQuery] string? queryScope, [FromBody] ObjectHistoryOutputRequest request,
        [FromServices] WorkspaceHistoryButtonHandler handler, CancellationToken token) =>
        HistoryButtonAsync(linkId, queryScope, "print", request.Filter, handler, token, output: request);

    // 每次重解正式来源；统一执行器检查同一状态后才进入实际查看或输出。
    private async Task<IResult> HistoryButtonAsync(string linkId, string? queryScope, string operation, ObjectHistoryFilter filter,
        WorkspaceHistoryButtonHandler handler, CancellationToken token, string? recordId = null, ObjectHistoryOutputRequest? output = null)
    {
        try
        {
            var input = new WorkspaceHistoryButtonInput(Actor(), ReadHistoryPosition(queryScope), linkId, operation, filter, recordId, output);
            return await buttonBusiness.ExecuteAsync(handler, input, token);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidPosition")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ArgumentException) { return Results.BadRequest(new ProblemDetails { Title = "履历筛选或输出范围无效。", Status = 400 }); }
    }

    // 正式执行绑定限定当前角色与阶段要求；来源owner每次以完整位置和LID重解真实资源，所有出口都重新检查。
    private async Task<(string ResourceKey, string ObjectId)> ResolveDeclaredHistoryTargetAsync(string linkId, string? queryScope,
        string execution, IDataExecutionAccess access, CancellationToken token)
    {
        var target = await contents.ResolvePermissionTargetAsync(Actor(), ReadHistoryPosition(queryScope), linkId, token);
        if (await access.EvaluateObjectAsync(new(execution, "workspace.backend", "read"),
            target.ResourceKey, "target", target.ObjectId, token) != DataExecutionDecision.Allowed)
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
