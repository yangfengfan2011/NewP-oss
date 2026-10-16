using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    /// <summary>受控确认仅接受真实来源选择及目标位置，不接收客户端对象类型或实体身份。</summary>
    [HttpPost("content/objects/copy/prepare"), RequireFeature("workspace.folder.pasteObjectCopy")]
    public async Task<IResult> PrepareObjectCopy(JsonElement input, CancellationToken token)
    {
        try
        {
            var request = ReadClipboardInput(input, true);
            return Results.Ok(await contents.PrepareObjectCopyAsync(Actor(), request.Mode, request.SourcePosition,
                request.LinkIds, request.TargetPosition!, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (FileLifecycleException exception) { return WorkspaceDocumentUploadsController.FileProblem(exception); }
    }

    /// <summary>唯一操作记录绑定来源及目标，提交仅接收正常可写值，成功回执不要求新对象可读。</summary>
    [HttpPost("content/objects/copy"), RequireFeature("workspace.folder.pasteObjectCopy")]
    public async Task<IResult> CopyObject(JsonElement input, CancellationToken token)
    {
        try
        {
            var id = ReadCopyOperationInput(input, true);
            var values = input.GetProperty("values").Deserialize<Dictionary<string, JsonElement>>()!;
            var result = await contents.SubmitObjectCopyAsync(Actor(), id, values, operations, token);
            return result.Succeeded ? Results.Ok(await contents.ObjectCopyResultAsync(Actor(), id, false, token))
                : Results.Json(result.Error, statusCode: result.Error!.StatusCode);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // writer被显式取消/回收中断不等同请求断线；先按持久操作事实返回真实取消或已提交结果。
            var receipt = await contents.ObjectCopyResultAsync(Actor(), ReadCopyOperationInput(input, true), false, token);
            return receipt.Succeeded ? Results.Ok(receipt) : LinkQueryEndpointExtensions.Problem(new LinkQueryException(409,
                receipt.State == "cancelled" ? "Workspace.CopyCancelled" : "Workspace.CopyOutcomeUnknown"));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (FileLifecycleException exception) { return WorkspaceDocumentUploadsController.FileProblem(exception); }
    }

    /// <summary>结果读取只核对本主体持久状态，不重放对象写入或泄露不可读结果。</summary>
    [HttpPost("content/objects/copy/result"), RequireFeature("workspace.folder.resolveObjectCopy")]
    public Task<IResult> ObjectCopyResult(JsonElement input, CancellationToken token) => ReadCopyResult(input, false, token);

    /// <summary>取消核对真实提交状态；提交已赢得竞争时继续报告已提交。</summary>
    [HttpPost("content/objects/copy/cancel"), RequireFeature("workspace.folder.pasteObjectCopy")]
    public Task<IResult> CancelObjectCopy(JsonElement input, CancellationToken token) => ReadCopyResult(input, true, token);

    // 状态接口复用同一主体验证和严格载荷，任何恢复动作都不会重新创建对象。
    private async Task<IResult> ReadCopyResult(JsonElement input, bool cancel, CancellationToken token)
    {
        try { return Results.Ok(await contents.ObjectCopyResultAsync(Actor(), ReadCopyOperationInput(input, false), cancel, token)); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (FileLifecycleException exception) { return WorkspaceDocumentUploadsController.FileProblem(exception); }
    }

    // 不接受任意字段包或授权布尔值；重复JSON成员也拒绝，避免不同解析器产生不同目标。
    private static string ReadCopyOperationInput(JsonElement input, bool writing)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length != (writing ? 2 : 1) || properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Any(item => item.Name != "operationId" && (!writing || item.Name != "values"))
            || !input.TryGetProperty("operationId", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
            || writing && (!input.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object
                || values.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != values.EnumerateObject().Count()))
            throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        return id.GetString()!;
    }
}
