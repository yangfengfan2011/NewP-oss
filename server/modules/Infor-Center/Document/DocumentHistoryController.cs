using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>独立对象履历仍交集同一read/history安全查询；EID无需Workspace来源，功能资格属于本owner。</summary>
[ApiController, Route("api/document/documents/{objectId}/history"), EndpointModule("Document")]
public sealed class DocumentHistoryController(IObjectHistoryQuery history, IObjectHistoryExport export) : ControllerBase
{
    private const string ResourceKey = "document.document";

    /// <summary>标准安全列表只接受已声明筛选，不接受其他对象或管理员查询参数。</summary>
    [HttpGet, RequireFeature("document.history.read")]
    public Task<IResult> List(string objectId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(async () => await history.ListAsync(ResourceKey, objectId, filter, token));

    /// <summary>记录ID与当前对象、筛选同时相交，每次重验独立history功能。</summary>
    [HttpGet("{recordId}"), RequireFeature("document.document.history")]
    public Task<IResult> Detail(string objectId, string recordId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(async () => await history.GetAsync(ResourceKey, objectId, recordId, filter, token));

    /// <summary>输出文件编码复用Audit稳定合同，不能引用私有实现或管理员payload。</summary>
    [HttpPost("export"), RequireFeature("document.document.history")]
    public async Task<IResult> Export(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            var file = await export.ExportAsync(ResourceKey, objectId, request, token);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        catch (ArgumentException) { return InvalidInput(); }
    }

    /// <summary>打印取得同一安全集合；浏览器受控视图承担实际print调用。</summary>
    [HttpPost("print"), RequireFeature("document.document.history")]
    public Task<IResult> Print(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token) =>
        ReadAsync(async () => await history.OutputAsync(ResourceKey, objectId, request, token) is { } items ? new { items } : null);

    private async Task<IResult> ReadAsync(Func<Task<object?>> read)
    {
        try
        {
            ValidateQuery();
            var value = await read();
            return value is null ? Results.NotFound() : Results.Ok(value);
        }
        catch (ArgumentException) { return InvalidInput(); }
    }

    private void ValidateQuery()
    {
        if (Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key is not
            ("pageIndex" or "pageSize" or "from" or "to" or "actorId" or "eventType" or "objectType" or "ipAddress" or "description")))
            throw new ArgumentException("履历查询参数无效。");
    }

    private static IResult InvalidInput() => Results.BadRequest(new ProblemDetails { Title = "履历筛选或输出范围无效。", Status = 400 });
}
