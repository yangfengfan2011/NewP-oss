using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>仅从 Document/Link 目标派生文件引用，原 raw Files 入口保持冻结。</summary>
[ApiController, Route("api/document/documents"), EndpointModule("Document"), PlatformRequestValidation]
public sealed class DocumentFilesController(IDocumentFiles files) : ControllerBase
{
    /// <summary>列表只要求现 read 功能与对象 read，不把 download 资格当读取条件。</summary>
    [HttpGet("{documentId}/files"), RequireFeature("document.document.read")]
    public async Task<IResult> List(string documentId, CancellationToken token)
    {
        if (Request.Query.Count > 0) return InvalidQuery();
        try { return Results.Ok(await files.ReadAsync(documentId, token)); }
        catch (FileLifecycleException exception) { return Problem(exception); }
    }

    /// <summary>独立重新裁决 download；框架在完成或取消时释放文件流。</summary>
    [HttpGet("{documentId}/files/{fileLinkId}/content"), RequireFeature("document.document.download")]
    public async Task<IResult> Download(string documentId, string fileLinkId, CancellationToken token)
    {
        if (Request.Query.Count > 0) return InvalidQuery();
        try
        {
            var content = await files.OpenAsync(documentId, fileLinkId, token);
            return Results.Stream(content.FileStream, content.ContentType, fileDownloadName: content.FileName);
        }
        catch (FileLifecycleException exception) { return Problem(exception); }
    }

    // 只接受固定当前版次目标，未知 query 不能伪装成任意版本或私有 owner 查询。
    private static IResult InvalidQuery() => Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Unknown query parameter."] },
        title: "REQUEST_VALIDATION_FAILED", extensions: new Dictionary<string, object?> { ["code"] = "REQUEST_VALIDATION_FAILED" });

    // 仅翻译安全业务错误，持久事实损坏或 I/O 缺失保持明确故障。
    private static IResult Problem(FileLifecycleException exception) => Results.Json(new
    {
        code = exception.Code, reasonKey = exception.ReasonKey, message = exception.Code,
        fieldErrors = exception.Field is null ? null : new[] { new { field = exception.Field, code = exception.Code, message = exception.Code } }
    }, statusCode: exception.StatusCode);
}
