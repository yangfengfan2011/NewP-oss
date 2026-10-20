using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>对象履历按自身声明交集完整读取；使用自身履历功能位置与EID，不借用Workspace出现位置或LID。</summary>
[ApiController, Route("api/document/documents/{objectId}/history"), EndpointModule("Document")]
public sealed class DocumentHistoryController(IObjectHistoryQuery history, IDataExecutionAccess dataAccess,
    IButtonBusinessExecutor buttons, DocumentHistoryButtonHandler handler) : ControllerBase
{
    private const string ResourceKey = "document.document";

    /// <summary>只读进入检查不签发凭据，正式出口仍重新核验其自身声明和当前对象。</summary>
    [HttpGet("check/{operation}"), RequireFeature("document.document.history"), ButtonBusinessHandler(typeof(DocumentHistoryButtonHandler))]
    public Task<IResult> Check(string objectId, string operation, CancellationToken token) =>
        operation is "enter" or "detail" or "preview" ? ButtonAsync(new(objectId, operation, new()), token) : Task.FromResult(InvalidInput());

    /// <summary>标准安全列表只接受已声明筛选，不接受其他对象或管理员查询参数。</summary>
    [HttpGet, RequireFeature("document.history.read")]
    public Task<IResult> List(string objectId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ReadAsync(objectId, "document.history.list", async () => await history.ListAsync(ResourceKey, objectId, filter, token), token);

    /// <summary>记录ID与当前对象、筛选同时相交，每次重验独立history功能。</summary>
    [HttpGet("{recordId}"), RequireFeature("document.document.history"), ButtonBusinessHandler(typeof(DocumentHistoryButtonHandler))]
    public Task<IResult> Detail(string objectId, string recordId, [FromQuery] ObjectHistoryFilter filter, CancellationToken token) =>
        ButtonAsync(new(objectId, "detail", filter, recordId), token);

    /// <summary>输出文件编码复用Audit稳定合同，不能引用私有实现或管理员payload。</summary>
    [HttpPost("export"), RequireFeature("document.document.history"), ButtonBusinessHandler(typeof(DocumentHistoryButtonHandler))]
    public Task<IResult> Export(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token) =>
        ButtonAsync(new(objectId, "export", request.Filter, Output: request), token);

    /// <summary>打印取得同一安全集合；浏览器受控视图承担实际print调用。</summary>
    [HttpPost("print"), RequireFeature("document.document.history"), ButtonBusinessHandler(typeof(DocumentHistoryButtonHandler))]
    public Task<IResult> Print(string objectId, [FromBody] ObjectHistoryOutputRequest request, CancellationToken token) =>
        ButtonAsync(new(objectId, "print", request.Filter, Output: request), token);

    // 每个按钮请求经统一执行器进入同一对象状态；列表继续保留独立读取合同。
    private async Task<IResult> ButtonAsync(DocumentHistoryButtonInput input, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            return await buttons.ExecuteAsync(handler, input, token);
        }
        catch (ActionDataAccessException error) { return DeclarationFailure(error); }
        catch (ArgumentException) { return InvalidInput(); }
    }

    // 列表、详情与打印共用声明核验和安全读取；取消及依赖故障继续传播。
    private async Task<IResult> ReadAsync(string objectId, string execution, Func<Task<object?>> read, CancellationToken token)
    {
        try
        {
            ValidateQuery();
            if (await dataAccess.EvaluateObjectAsync(new(execution, "document.backend", "read"),
                ResourceKey, "target", objectId, token) != DataExecutionDecision.Allowed) return Unavailable();
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

    // 对象不存在与读取拒绝统一返回不可用，避免泄露对象存在性。
    private static IResult Unavailable() => Results.NotFound(new ProblemDetails { Status = 404, Title = "对象不存在或不可访问。",
        Extensions = { ["reasonKey"] = "ObjectHistory.Unavailable" } });

    // 声明核验错误保留服务端状态与代码，不降级为对象不可用。
    private static IResult DeclarationFailure(ActionDataAccessException error) => Results.Problem(statusCode: error.Status,
        title: error.Code, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
