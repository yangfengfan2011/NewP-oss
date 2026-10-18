using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>独立履历以自身声明和完整读取验证EID；不借用Workspace来源或功能资格。</summary>
[ApiController, Route("api/document/documents/{objectId}/history"), EndpointModule("Document")]
public sealed class DocumentHistoryController(IObjectHistoryQuery history, IObjectHistoryExport export,
    IActionDataAccess access) : ControllerBase
{
    private const string ResourceKey = "document.document";

    /// <summary>点击时只检查当前声明与完整读取；不签发可复用的执行凭据。</summary>
    [HttpGet("check/{operation}"), RequireFeature("document.document.history")]
    public Task<IResult> Check(string objectId, string operation, CancellationToken token) =>
        operation is "enter" or "preview" or "detail"
            ? ReadAsync(objectId, "document.history." + operation, () => Task.FromResult<object?>(true), token, noContent: true)
            : Task.FromResult<IResult>(Results.BadRequest());

    /// <summary>标准安全列表只接受已声明筛选，不接受其他对象或管理员查询参数。</summary>
    [HttpGet, RequireFeature("document.history.read")]
    public Task<IResult> List(string objectId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(objectId, "document.history.list", async () => await history.ListAsync(ResourceKey, objectId, filter, token), token);

    /// <summary>记录ID与当前对象、筛选同时相交，每次重验独立history功能。</summary>
    [HttpGet("{recordId}"), RequireFeature("document.document.history")]
    public Task<IResult> Detail(string objectId, string recordId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(objectId, "document.history.detail", async () => await history.GetAsync(ResourceKey, objectId, recordId, filter, token), token);

    /// <summary>输出文件编码复用Audit稳定合同，不能引用私有实现或管理员payload。</summary>
    [HttpPost("export"), RequireFeature("document.document.history")]
    public async Task<IResult> Export(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            if (!await access.IsAllowedAsync("document.history.export", ResourceKey, objectId, token)) return Results.NotFound();
            var file = await export.ExportAsync(ResourceKey, objectId, request, token);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    /// <summary>打印取得同一安全集合；浏览器受控视图承担实际print调用。</summary>
    [HttpPost("print"), RequireFeature("document.document.history")]
    public Task<IResult> Print(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token) =>
        ReadAsync(objectId, "document.history.print", async () => await history.OutputAsync(ResourceKey, objectId, request, token) is { } items ? new { items } : null, token);

    // 所有出口从当前正式声明重判；安全查询还会重验对象读取及记录关联。
    private async Task<IResult> ReadAsync(string objectId, string execution, Func<Task<object?>> read, CancellationToken token, bool noContent = false)
    {
        try
        {
            ValidateQuery();
            if (!await access.IsAllowedAsync(execution, ResourceKey, objectId, token)) return Results.NotFound();
            var value = await read();
            return value is null ? Results.NotFound() : noContent ? Results.NoContent() : Results.Ok(value);
        }
        catch (ActionDataAccessException exception) { return Results.Problem(statusCode: exception.Status, title: exception.Code); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    // 独立EID入口只接受履历筛选，不能用Workspace来源或任意客户端资源替换目标。
    private void ValidateQuery()
    {
        if (Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key is not
            ("pageIndex" or "pageSize" or "from" or "to" or "actorId" or "eventType" or "objectType" or "ipAddress" or "description")))
            throw new ArgumentException("履历查询参数无效。");
    }

    private static IResult InvalidInput() => Results.BadRequest(new ProblemDetails { Title = "履历筛选或输出范围无效。", Status = 400 });
}
