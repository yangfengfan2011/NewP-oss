using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式位置准入先于文件 I/O，手工解析有界元数据并将非 seek 文件流交给 Files。</summary>
[ApiController, Route("api/workspace/content/document-uploads"), EndpointModule("Workspace"), PlatformRequestValidation]
public sealed partial class WorkspaceDocumentUploadsController(IFileLifecycleService files, IButtonBusinessExecutor buttons, WorkspaceDocumentUploadHandler uploadHandler,
    ILogger<WorkspaceDocumentUploadsController> logger) : ControllerBase
{
    private const int MetadataBudget = 65536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>只接受 position、values 与单 file 的顺序；多余 part 会取消已暂存文件而不会返回假成功。</summary>
    [HttpPost, RequireFeature("workspace.folder.createDocument"), WorkspaceDocumentUploadLimits, ButtonBusinessHandler(typeof(WorkspaceDocumentUploadHandler))]
    public async Task<IResult> Stage(CancellationToken token)
    {
        StagedFileInfo? staged = null;
        try
        {
            if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var media)
                || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw new JsonException("Multipart is required.");
            var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) throw new JsonException("Multipart boundary is invalid.");
            using var body = new WorkspaceUploadRequestStream(Request.Body, checked(files.MaxUploadSizeBytes + 131072));
            var reader = new MultipartReader(boundary, body) { HeadersLengthLimit = MetadataBudget, HeadersCountLimit = 16 };
            var positionPart = await RequireSectionAsync(reader, "position", false, token);
            var positionBytes = await ReadJsonBytesAsync(positionPart.Body, MetadataBudget, token);
            using var positionJson = JsonDocument.Parse(positionBytes);
            var position = WorkspaceDocumentPositionConverter.Read(positionJson.RootElement);
            var valuesPart = await RequireSectionAsync(reader, "values", false, token);
            var valuesBytes = await ReadJsonBytesAsync(valuesPart.Body, MetadataBudget - positionBytes.Length, token);
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(valuesBytes, JsonOptions) ?? throw new JsonException("Values are required.");
            var filePart = await RequireSectionAsync(reader, "file", true, token);
            var disposition = ContentDispositionHeaderValue.Parse(filePart.ContentDisposition!);
            var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value!;
            staged = await buttons.ExecuteAsync(uploadHandler,
                new WorkspaceDocumentUploadInput(Actor(), position, values, filePart.Body, fileName, filePart.ContentType ?? "application/octet-stream"), token);
            if (await ReadSectionAsync(reader, token) is not null) throw new JsonException("Unexpected multipart section.");
            // final boundary 后的 epilogue 仍计入真实请求开销，chunked 也不能绕过预算。
            await body.CopyToAsync(Stream.Null, token);
            if (body.BytesRead - staged.Length - positionBytes.Length - valuesBytes.Length > MetadataBudget)
                throw new FileLifecycleException(413, "Files.UploadTooLarge");
            return Results.Ok(staged);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FileLifecycleException or LinkQueryException or ActionDataAccessException or WorkspaceCreationRejectedException or OperationCanceledException)
        {
            if (staged is not null) await CancelRejectedAsync(staged.UploadId);
            if (exception is OperationCanceledException) throw;
            return exception switch
            {
                WorkspaceCreationRejectedException rejected => Results.Json(rejected.Error, statusCode: rejected.Error.StatusCode),
                FileLifecycleException file => FileProblem(file),
                LinkQueryException link => LinkQueryEndpointExtensions.Problem(link),
                ActionDataAccessException access => Results.Problem(statusCode: access.Status, title: access.Code,
                    extensions: new Dictionary<string, object?> { ["code"] = access.Code }),
                _ => ValidationProblem(exception is JsonException ? "Invalid multipart metadata or shape." : "Invalid multipart framing.")
            };
        }
    }

    /// <summary>仅当前 actor 的未确认 lease 可取消；它不提供使用或文件删除旁路。</summary>
    [HttpDelete("{uploadId}"), RequireFeature("workspace.folder.createDocument")]
    public async Task<IResult> Cancel(string uploadId, CancellationToken token)
    {
        if (!UploadId().IsMatch(uploadId)) return ValidationProblem("Invalid upload identity.");
        try { await files.CancelStageAsync(uploadId, token); return Results.NoContent(); }
        catch (FileLifecycleException exception) { return FileProblem(exception); }
    }

    // 拒绝迟到多余 part 后只取消本次已知暂存；结果未知的 Stage 自己按 durable state 收敛。
    private async Task CancelRejectedAsync(string uploadId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await files.CancelStageAsync(uploadId, cleanup.Token); }
        catch (Exception exception) { logger.LogWarning("DOCUMENT_UPLOAD_CANCEL_UNAVAILABLE: {FailureType}", exception.GetType().Name); }
    }

    // 每项具有精确名称和文件形状，顺序与重复校验先于下一项消费。
    private static async Task<MultipartSection> RequireSectionAsync(MultipartReader reader, string name, bool isFile, CancellationToken token)
    {
        var part = await ReadSectionAsync(reader, token);
        if (part is null || !ContentDispositionHeaderValue.TryParse(part.ContentDisposition, out var disposition)
            || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(disposition.Name).Value != name
            || (disposition.FileName.HasValue || disposition.FileNameStar.HasValue) != isFile)
            throw new JsonException("Multipart section is missing or out of order.");
        part.Body = new MultipartInputStream(part.Body);
        return part;
    }

    // 只翻译解析器的输入读取异常；Files 写盘和改名不在此边界内。
    private static async Task<MultipartSection?> ReadSectionAsync(MultipartReader reader, CancellationToken token)
    {
        try { return await reader.ReadNextSectionAsync(token); }
        catch (IOException exception) when (exception is not BadHttpRequestException)
        { throw new InvalidDataException("Invalid multipart section framing.", exception); }
    }

    // 只转换截断输入；宿主 HTTP 拒绝、存储 I/O 故障和取消保留原结果。
    private sealed class MultipartInputStream(Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return source.Read(buffer, offset, count); }
            catch (IOException exception) when (exception is not BadHttpRequestException)
            { throw new InvalidDataException("Invalid multipart body framing.", exception); }
        }
        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            try { return await source.ReadAsync(buffer, token); }
            catch (IOException exception) when (exception is not BadHttpRequestException)
            { throw new InvalidDataException("Invalid multipart body framing.", exception); }
        }
        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException();
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // 只有有界 JSON 可缓冲；在解析前检测合计 UTF-8 字节预算。
    private static async Task<byte[]> ReadJsonBytesAsync(Stream body, int limit, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await body.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new FileLifecycleException(413, "Files.UploadTooLarge");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return output.ToArray();
    }

    // 手工 JSON 错误沿正式新 API 请求校验契约，不泄漏 parser 或内部身份细节。
    private static IResult ValidationProblem(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["input"] = [message] },
        title: "REQUEST_VALIDATION_FAILED", extensions: new Dictionary<string, object?> { ["code"] = "REQUEST_VALIDATION_FAILED" });

    /// <summary>Workspace 文件边界只翻译安全业务错误，持久损坏继续传播。</summary>
    internal static IResult FileProblem(FileLifecycleException exception) => Results.Json(new
    {
        code = exception.Code, reasonKey = exception.ReasonKey, message = exception.Code,
        fieldErrors = exception.Field is null ? null : new[] { new { field = exception.Field, code = exception.Code, message = exception.Code } }
    }, statusCode: exception.StatusCode);

    // actor 永远从已认证服务端主体取得。
    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");

    // 不接受大小写变体、其他模型前缀或任意私有存储键。
    [GeneratedRegex("\\AFileUploadLease_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex UploadId();
}
