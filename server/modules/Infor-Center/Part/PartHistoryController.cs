using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Part;

/// <summary>独立对象履历按自身声明交集完整读取；EID无需Workspace来源，功能资格属于本owner。</summary>
[ApiController, Route("api/part/parts/{objectId}/history"), EndpointModule("Part")]
public sealed class PartHistoryController(IObjectHistoryQuery history, IObjectHistoryExport export, IActionDataAccess dataAccess) : ControllerBase
{
    private const string ResourceKey = "part.part";

    /// <summary>只读进入检查不签发凭据，正式出口仍重新核验其自身声明和当前对象。</summary>
    [HttpGet("check/{operation}"), RequireFeature("part.part.history")]
    public async Task<IResult> Check(string objectId, string operation, CancellationToken token)
    {
        if (operation is not ("enter" or "detail" or "preview")) return InvalidInput();
        try
        {
            ValidateQuery();
            return await dataAccess.IsAllowedAsync("part.history." + operation, ResourceKey, objectId, token)
                ? Results.NoContent() : Unavailable();
        }
        catch (ActionDataAccessException error) { return DeclarationFailure(error); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    /// <summary>标准安全列表只接受已声明筛选，不接受其他对象或管理员查询参数。</summary>
    [HttpGet, RequireFeature("part.history.read")]
    public Task<IResult> List(string objectId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(objectId, "part.history.list", async () => await history.ListAsync(ResourceKey, objectId, filter, token), token);

    /// <summary>记录ID与当前对象、筛选同时相交，每次重验独立history功能。</summary>
    [HttpGet("{recordId}"), RequireFeature("part.part.history")]
    public Task<IResult> Detail(string objectId, string recordId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(objectId, "part.history.detail", async () => await history.GetAsync(ResourceKey, objectId, recordId, filter, token), token);

    /// <summary>输出文件编码复用Audit稳定合同，不能引用私有实现或管理员payload。</summary>
    [HttpPost("export"), RequireFeature("part.part.history")]
    public async Task<IResult> Export(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            if (!await dataAccess.IsAllowedAsync("part.history.export", ResourceKey, objectId, token)) return Unavailable();
            var file = await export.ExportAsync(ResourceKey, objectId, request, token);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (ActionDataAccessException error) { return DeclarationFailure(error); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    /// <summary>打印取得同一安全集合；浏览器受控视图承担实际print调用。</summary>
    [HttpPost("print"), RequireFeature("part.part.history")]
    public Task<IResult> Print(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token) =>
        ReadAsync(objectId, "part.history.print", async () => await history.OutputAsync(ResourceKey, objectId, request, token) is { } items ? new { items } : null, token);

    private async Task<IResult> ReadAsync(string objectId, string execution, Func<Task<object?>> read, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            if (!await dataAccess.IsAllowedAsync(execution, ResourceKey, objectId, token)) return Unavailable();
            var value = await read();
            return value is null ? Results.NotFound() : Results.Ok(value);
        }
        catch (ActionDataAccessException error) { return DeclarationFailure(error); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    private void ValidateQuery()
    {
        if (Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key is not
            ("pageIndex" or "pageSize" or "from" or "to" or "actorId" or "eventType" or "objectType" or "ipAddress" or "description")))
            throw new ArgumentException("履历查询参数无效。");
    }

    private static IResult InvalidInput() => Results.BadRequest(new ProblemDetails { Title = "履历筛选或输出范围无效。", Status = 400 });

    private static IResult Unavailable() => Results.NotFound(new ProblemDetails { Status = 404, Title = "对象不存在或不可访问。",
        Extensions = { ["reasonKey"] = "ObjectHistory.Unavailable" } });

    private static IResult DeclarationFailure(ActionDataAccessException error) => Results.Problem(statusCode: error.Status,
        title: error.Code, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
