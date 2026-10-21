using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk.Http;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式创建仅接受当前出现位置与可写字段；所有业务规则仍在应用服务。</summary>
[ApiController, Route("api/workspace"), EndpointModule("Workspace"), PlatformRequestValidation]
public sealed partial class WorkspaceContentsController(WorkspaceContents contents, WorkspaceSpaces spaces, WorkspacePositions positions,
    ILogger<WorkspaceContentsController> logger, WorkspaceFolderScope folderScope, IObjectOperationExecutor operations, ICP.Platform.Crud.Runtime.LinkTreeReader trees,
    ICP.Platform.Authorization.Contracts.IButtonBusinessExecutor buttonBusiness, WorkspaceReorderHandler reorderHandler, WorkspaceMutationHandler mutationHandler, WorkspaceEditValuesHandler editValuesHandler,
    WorkspacePartCreationHandler partCreationHandler, WorkspaceDocumentCreationHandler documentCreationHandler, WorkspaceFolderCreationHandler folderCreationHandler, WorkspaceRemoveReferencesHandler removeReferencesHandler,
    WorkspaceClipboardSelectionHandler clipboardSelectionHandler, WorkspacePasteReferencesHandler pasteReferencesHandler,
    ICP.Platform.Authorization.Contracts.IButtonExecutionAccess copyAccess, WorkspaceObjectCopyResultHandler copyResultHandler,
    ICP.Platform.Authorization.Contracts.IDataScopeEvaluationScope? evaluation = null) : ControllerBase
{
    public sealed record CreateFolderInput(WorkspacePosition Position, Dictionary<string, JsonElement> Values)
    {
        public bool? CopyParentAuthorization { get; init; }
    }
    public sealed record AddReferenceInput(WorkspacePosition Position, LinkObjectReference Target);
    public sealed record RemoveReferenceInput(WorkspacePosition Position);
    public sealed record DeleteObjectsInput(WorkspacePosition Position, string[] LinkIds);

    /// <summary>只读复制预检使用正式copy功能，快照仅用于本次页面交互，不生成持久凭证。</summary>
    [HttpPost("content/clipboard/validate"), RequireFeature("workspace.folder.copy")]
    [ButtonBusinessHandler(typeof(WorkspaceClipboardSelectionHandler))]
    public async Task<IResult> ValidateClipboard(JsonElement input, CancellationToken token)
    {
        try
        {
            var request = ReadClipboardInput(input, false);
            if (request.Mode != "copy") throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            return Results.Ok(await buttonBusiness.ExecuteAsync(clipboardSelectionHandler,
                new WorkspaceClipboardSelectionInput(Actor(), request.Mode, request.SourcePosition, request.LinkIds), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception) { return ReferenceButtonProblem(exception); }
    }

    /// <summary>剪切预检绑定自己的正式功能，客户端mode或复制功能位置不能替代cut资格。</summary>
    [HttpPost("content/clipboard/cut/validate"), RequireFeature("workspace.folder.cut")]
    [ButtonBusinessHandler(typeof(WorkspaceClipboardSelectionHandler))]
    public async Task<IResult> ValidateCutClipboard(JsonElement input, CancellationToken token)
    {
        try
        {
            var request = ReadClipboardInput(input, false);
            if (request.Mode != "cut") throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            return Results.Ok(await buttonBusiness.ExecuteAsync(clipboardSelectionHandler,
                new WorkspaceClipboardSelectionInput(Actor(), request.Mode, request.SourcePosition, request.LinkIds), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception) { return ReferenceButtonProblem(exception); }
    }

    /// <summary>粘贴仅接受来源与目标完整位置和LID，提交后的展示读取不参与写入回执。</summary>
    [HttpPost("content/paste"), RequireFeature("workspace.folder.paste")]
    [ButtonBusinessHandler(typeof(WorkspacePasteReferencesHandler))]
    public async Task<IResult> Paste(JsonElement input, CancellationToken token)
    {
        try
        {
            var request = ReadClipboardInput(input, true);
            return Results.Ok(await buttonBusiness.ExecuteAsync(pasteReferencesHandler,
                new WorkspacePasteReferencesInput(Actor(), request.Mode, request.SourcePosition, request.LinkIds, request.TargetPosition!), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception) { return ReferenceButtonProblem(exception); }
    }

    /// <summary>名称输入只读准备返回本次已核证源和目标，不形成随后提交的授权凭证。</summary>
    [HttpPost("content/folders/copy/prepare"), RequireFeature("workspace.folder.pasteFolderCopy")]
    [ButtonBusinessHandler(typeof(WorkspaceFolderCopyPreparationHandler))]
    public async Task<IResult> PrepareFolderCopy(JsonElement input, CancellationToken token)
    {
        try
        {
            copyAccess.BindHandler(typeof(WorkspaceFolderCopyPreparationHandler));
            var request = ReadClipboardInput(input, true);
            return Results.Ok(await contents.PrepareFolderCopyAsync(Actor(), request.Mode, request.SourcePosition, request.LinkIds, request.TargetPosition!, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>单源浅副本仅接受确切来源、目标位置及新根名称；提交后不附加新对象读取要求。</summary>
    [HttpPost("content/folders/copy"), RequireFeature("workspace.folder.pasteFolderCopy")]
    [ButtonBusinessHandler(typeof(WorkspaceFolderCopyHandler))]
    public async Task<IResult> CopyFolder(JsonElement input, CancellationToken token)
    {
        try
        {
            copyAccess.BindHandler(typeof(WorkspaceFolderCopyHandler));
            if (input.ValueKind != JsonValueKind.Object) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            var members = input.EnumerateObject().ToArray();
            if (members.Length is not (5 or 6) || members.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count() != members.Length
                || members.Any(member => member.Name is not ("mode" or "sourcePosition" or "linkIds" or "targetPosition" or "name" or "copyParentAuthorization"))
                || input.TryGetProperty("copyParentAuthorization", out var copyParent) && copyParent.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !input.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            var clipboard = JsonSerializer.SerializeToElement(members.Where(member => member.Name is not ("name" or "copyParentAuthorization")).ToDictionary(member => member.Name, member => member.Value));
            var request = ReadClipboardInput(clipboard, true);
            return Results.Ok(await contents.CopyFolderAsync(Actor(), request.Mode, request.SourcePosition, request.LinkIds, request.TargetPosition!, name.GetString()!, copyParent.ValueKind == JsonValueKind.True, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>名称输入只读准备返回本次已核证源和目标，不形成随后提交的授权凭证。</summary>
    [HttpPost("content/folders/copy-structure/prepare"), RequireFeature("workspace.folder.pasteFolderStructure")]
    public async Task<IResult> PrepareFolderStructure(JsonElement input, CancellationToken token)
    {
        try
        {
            var request = ReadClipboardInput(input, true);
            return Results.Ok(await contents.PrepareFolderStructureAsync(Actor(), request.Mode, request.SourcePosition, request.LinkIds, request.TargetPosition!, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>单源结构复制仅接受确切来源、目标位置及新根名称；提交后不附加新对象读取要求。</summary>
    [HttpPost("content/folders/copy-structure"), RequireFeature("workspace.folder.pasteFolderStructure")]
    public async Task<IResult> CopyFolderStructure(JsonElement input, CancellationToken token)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            var members = input.EnumerateObject().ToArray();
            if (members.Length is not (5 or 6) || members.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count() != members.Length
                || members.Any(member => member.Name is not ("mode" or "sourcePosition" or "linkIds" or "targetPosition" or "name" or "copyParentAuthorization"))
                || input.TryGetProperty("copyParentAuthorization", out var copyParent) && copyParent.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !input.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            var clipboard = JsonSerializer.SerializeToElement(members.Where(member => member.Name is not ("name" or "copyParentAuthorization")).ToDictionary(member => member.Name, member => member.Value));
            var request = ReadClipboardInput(clipboard, true);
            return Results.Ok(await contents.CopyFolderStructureAsync(Actor(), request.Mode, request.SourcePosition, request.LinkIds, request.TargetPosition!, name.GetString()!, copyParent.ValueKind == JsonValueKind.True, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    private sealed record ClipboardInput(string Mode, WorkspacePosition SourcePosition, string[] LinkIds, WorkspacePosition? TargetPosition);

    // 两个命令共用严格形状，未知/重复成员和伪造类型、对象身份不能作为后续写入输入。
    private static ClipboardInput ReadClipboardInput(JsonElement input, bool paste)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object) throw new JsonException();
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var member in input.EnumerateObject())
                if (member.Name is not ("mode" or "sourcePosition" or "linkIds") && (!paste || member.Name != "targetPosition")
                    || !fields.TryAdd(member.Name, member.Value)) throw new JsonException();
            if (fields.Count != (paste ? 4 : 3) || !fields.TryGetValue("mode", out var mode) || mode.ValueKind != JsonValueKind.String
                || !fields.TryGetValue("sourcePosition", out var source) || !fields.TryGetValue("linkIds", out var ids)
                || ids.ValueKind != JsonValueKind.Array || ids.EnumerateArray().Any(id => id.ValueKind != JsonValueKind.String)) throw new JsonException();
            return new(mode.GetString()!, WorkspaceDocumentPositionConverter.Read(source), ids.EnumerateArray().Select(id => id.GetString()!).ToArray(),
                paste ? WorkspaceDocumentPositionConverter.Read(fields["targetPosition"]) : null);
        }
        catch (JsonException) { throw new LinkQueryException(400, "Workspace.InvalidClipboardInput"); }
    }

    /// <summary>编辑装载重新验证当前出现位置与对象 edit 资格。</summary>
    [HttpPost("content/{linkId}/edit-values"), RequireFeature("workspace.folder.editObject"), ButtonBusinessHandler(typeof(WorkspaceEditValuesHandler))]
    public async Task<IResult> EditValues(string linkId, RemoveReferenceInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null) throw new LinkQueryException(400, "Workspace.InvalidEditInput");
            return Results.Ok(new { fields = await buttonBusiness.ExecuteAsync(editValuesHandler, new WorkspaceEditValuesInput(Actor(), input.Position, linkId), token) });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>编辑使用当前 LID 定位真实对象；字段规则由对象拥有者执行。</summary>
    [HttpPut("content/{linkId}"), RequireFeature("workspace.folder.editObject"), ButtonBusinessHandler(typeof(WorkspaceMutationHandler))]
    public Task<IResult> EditObject(string linkId, CreateFolderInput input, CancellationToken token) =>
        MutateObjectsAsync(input.Position, [linkId], input.Values, ObjectOperationAction.Update, token);

    /// <summary>单行删除与批量命令共享原子对象删除规则。</summary>
    [HttpDelete("content/{linkId}"), RequireFeature("workspace.folder.deleteObject"), ButtonBusinessHandler(typeof(WorkspaceMutationHandler))]
    public Task<IResult> DeleteObject(string linkId, RemoveReferenceInput input, CancellationToken token) =>
        MutateObjectsAsync(input.Position, [linkId], null, ObjectOperationAction.Delete, token);

    /// <summary>混合选择整批原子删除，清理所有引用；不递归删除非空文件夹。</summary>
    [HttpPost("content/delete"), RequireFeature("workspace.folder.deleteObjects"), ButtonBusinessHandler(typeof(WorkspaceMutationHandler))]
    public Task<IResult> DeleteObjects(DeleteObjectsInput input, CancellationToken token) =>
        MutateObjectsAsync(input.Position, input.LinkIds, null, ObjectOperationAction.Delete, token);

    // 服务端构造可信上下文；成功后只返回回执，刷新读取不参与写入结果。
    private async Task<IResult> MutateObjectsAsync(WorkspacePosition? position, string[]? linkIds,
        Dictionary<string, JsonElement>? values, ObjectOperationAction action, CancellationToken token)
    {
        try
        {
            if (position is null || linkIds is null || action == ObjectOperationAction.Update && values is null)
                throw new LinkQueryException(400, "Workspace.InvalidEditInput");
            var result = await buttonBusiness.ExecuteAsync(mutationHandler, new WorkspaceMutationInput(Actor(), position, linkIds, values, action), token);
            return result.Succeeded ? Results.Ok(new { succeeded = true }) : Results.Json(result.Error, statusCode: result.Error!.StatusCode);
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>在当前文件夹内创建真实零组件，所属对象行为与内容关系共享事务。</summary>
    [HttpPost("content/parts"), RequireFeature("workspace.folder.createPart"), ButtonBusinessHandler(typeof(WorkspacePartCreationHandler))]
    public Task<IResult> CreatePart(CreateFolderInput input, CancellationToken token) => CreateObjectAsync(input, WorkspaceObjectCreation.CreatePart, token);

    /// <summary>在当前文件夹内创建真实图文档，不通过两个 HTTP 写入拼接。</summary>
    [HttpPost("content/documents"), RequireFeature("workspace.folder.createDocument"), ButtonBusinessHandler(typeof(WorkspaceDocumentCreationHandler))]
    public async Task<IResult> CreateDocument(CreateWorkspaceDocumentInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.Values is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["input"] = ["Position and values are required."] }, title: "REQUEST_VALIDATION_FAILED", extensions: new Dictionary<string, object?> { ["code"] = "REQUEST_VALIDATION_FAILED" });
            var result = await buttonBusiness.ExecuteAsync(documentCreationHandler,
                new WorkspaceDocumentCreationInput(Actor(), input.Position, input.Values, input.Uploads), token);
            return result.Succeeded ? Results.Ok(new { succeeded = true }) : Results.Json(result.Error, statusCode: result.Error!.StatusCode);
        }
        catch (WorkspaceCreationRejectedException exception) { return Results.Json(exception.Error, statusCode: exception.Error.StatusCode); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (FileLifecycleException exception) { return WorkspaceDocumentUploadsController.FileProblem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    // 唯一执行器提交后只返回成功回执；业务校验保留所属模块的标准字段错误。
    private async Task<IResult> CreateObjectAsync(CreateFolderInput input, ObjectOperationAction action, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.Values is null) throw new LinkQueryException(400, "Workspace.InvalidCreateInput");
            var result = await buttonBusiness.ExecuteAsync(partCreationHandler, new WorkspacePartCreationInput(Actor(), input.Position, input.Values), token);
            return result.Succeeded ? Results.Ok(new { succeeded = true }) : Results.Json(result.Error, statusCode: result.Error!.StatusCode);
        }
        catch (WorkspaceCreationRejectedException exception) { return Results.Json(exception.Error, statusCode: exception.Error.StatusCode); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>添加只提交引用；回执不绑定提交后的读取资格，不会因刷新故障重放命令。</summary>
    [HttpPost("content/references"), RequireFeature("workspace.folder.add")]
    public async Task<IResult> AddReference(AddReferenceInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.Target is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
            await contents.AddReferenceAsync(Actor(), input.Position, input.Target, token);
            return Results.Ok(new { succeeded = true });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>正式排序权限独立于列表位置；服务器不接受客户端邻居、序号或批量选择。</summary>
    [HttpPost("content/tree-order"), RequireFeature("workspace.folder.reorder"), ButtonBusinessHandler(typeof(WorkspaceReorderHandler))]
    public async Task<IResult> Reorder(JsonElement input, CancellationToken token)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != 3
                || !input.TryGetProperty("parentPosition", out var parent) || !input.TryGetProperty("linkId", out var linkId)
                || !input.TryGetProperty("direction", out var direction) || parent.ValueKind != JsonValueKind.Object
                || parent.EnumerateObject().Count() != 3 || !parent.TryGetProperty("spaceId", out var space)
                || !parent.TryGetProperty("rootFolderId", out var root) || !parent.TryGetProperty("linkIds", out var chain)
                || space.ValueKind != JsonValueKind.String || root.ValueKind != JsonValueKind.String || chain.ValueKind != JsonValueKind.Array
                || chain.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)
                || linkId.ValueKind != JsonValueKind.String || direction.ValueKind != JsonValueKind.String)
                throw new LinkQueryException(400, "Workspace.InvalidTreeOrderInput");
            var position = new WorkspacePosition(space.GetString()!, root.GetString()!, chain.EnumerateArray().Select(item => item.GetString()!).ToArray());
            return Results.Ok(await buttonBusiness.ExecuteAsync(reorderHandler,
                new WorkspaceReorderInput(Actor(), position, linkId.GetString()!, direction.GetString()!), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            var code = exception.Code switch { "DataScope.ActionDenied" => "Workspace.ParentWriteDenied", "StandardCrud.NotFound" => "Workspace.PositionUnavailable", _ => exception.Code };
            return Results.Problem(statusCode: exception.Status, title: code,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }
    }

    /// <summary>移除所选关系始终带当前完整位置，裸父或对象 EID 不能替代 LID。</summary>
    [HttpDelete("content/references/{linkId}"), RequireFeature("workspace.folder.remove")]
    [ButtonBusinessHandler(typeof(WorkspaceRemoveReferencesHandler))]
    public async Task<IResult> RemoveReference(string linkId, RemoveReferenceInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
            await buttonBusiness.ExecuteAsync(removeReferencesHandler, new WorkspaceRemoveReferencesInput(Actor(), input.Position, [linkId]), token);
            return Results.Ok(new { succeeded = true });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception) { return ReferenceButtonProblem(exception); }
    }

    /// <summary>整批只移除当前父中的引用，业务对象、其他入向关系及文件保留。</summary>
    [HttpPost("content/references/remove"), RequireFeature("workspace.folder.removeSelected")]
    [ButtonBusinessHandler(typeof(WorkspaceRemoveReferencesHandler))]
    public async Task<IResult> RemoveReferences(DeleteObjectsInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.LinkIds is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
            await buttonBusiness.ExecuteAsync(removeReferencesHandler, new WorkspaceRemoveReferencesInput(Actor(), input.Position, input.LinkIds), token);
            return Results.Ok(new { succeeded = true });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception) { return ReferenceButtonProblem(exception); }
    }

    // 只翻译本次父目标的既有业务响应，判权依赖故障仍沿原异常边界传播。
    private static IResult ReferenceButtonProblem(ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
    {
        var code = exception.Code switch { "DataScope.ActionDenied" => "Workspace.ParentWriteDenied", "StandardCrud.NotFound" => "Workspace.PositionUnavailable", _ => exception.Code };
        return Results.Problem(statusCode: exception.Status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    /// <summary>正式单选候选只返回安全字段，不能请求来源、私人路径或任意排序表达式。</summary>
    [HttpGet("folders/candidates"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> Candidates([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken token = default)
    {
        try
        {
            if (Request.Query.Any(pair => pair.Key is not ("pageIndex" or "pageSize" or "keyword") || pair.Value.Count != 1))
                throw new LinkQueryException(400, "Workspace.InvalidCandidateQuery");
            return Results.Ok(await folderScope.CandidatesAsync(Actor(), pageIndex, pageSize, keyword, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>创建已提交后展示失败只返回成功回执，不泄露结果、不重放创建。</summary>
    [HttpPost("content/folders"), RequireFeature("workspace.folder.create"), ButtonBusinessHandler(typeof(WorkspaceFolderCreationHandler))]
    public async Task<IResult> CreateFolder(CreateFolderInput input, CancellationToken token)
    {
        string id;
        try
        {
            if (input.Position is null || input.Values is null) throw new LinkQueryException(400, "Workspace.InvalidCreateInput");
            id = await buttonBusiness.ExecuteAsync(folderCreationHandler,
                new WorkspaceFolderCreationInput(Actor(), input.Position, input.Values, input.CopyParentAuthorization == true), token);
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
        try
        {
            using var observation = evaluation?.BeginReadAfterWrite();
            var fields = await spaces.ReadFolderAsync(Actor(), id, token);
            return fields is null ? Results.Ok(new { succeeded = true }) : Results.Ok(new { id, fields });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("WORKSPACE_POST_WRITE_READ_UNAVAILABLE: {FailureType}", exception.GetType().Name);
            return Results.Ok(new { succeeded = true });
        }
    }

    /// <summary>共享详情按真实 Folder EID 读取，不接受 LID 代替对象身份。</summary>
    [HttpGet("folders/{id}"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> Folder(string id, CancellationToken token)
    {
        if (Request.Query.Count > 0) return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidDetailQuery"));
        try
        {
            var fields = await spaces.ReadFolderFromEntryAsync(Actor(), id, token);
            return fields is null ? Results.NotFound() : Results.Ok(new { id, fields });
        }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>受信任树入口完整读取当前层，只允许文件夹类型；右侧筛选不能改变树计划。</summary>
    [HttpGet("content/children"), RequireFeature("tree:workspace.structure")]
    public async Task<IResult> Children([FromQuery] string spaceId, [FromQuery] string rootFolderId, [FromQuery] string linkIds, CancellationToken token)
    {
        try
        {
            var scope = new Dictionary<string, string?> { ["spaceId"] = spaceId, ["rootFolderId"] = rootFolderId, ["linkIds"] = linkIds };
            if (Request.Query.Any(q => !scope.ContainsKey(q.Key) || q.Value.Count != 1)) throw new LinkQueryException(400, "Workspace.InvalidPosition");
            var path = await positions.ResolveAsync(Actor(), WorkspacePositions.FromScope(scope), token);
            var nodes = await trees.ChildrenFromApiAsync("workspace.structure", new(spaceId, rootFolderId, WorkspacePositions.FromScope(scope).LinkIds),
                new(new("workspace.content.children.api", "workspace.backend", "read"), "workspace.content.children.api", "query"), token);
            var parent = new LinkObjectReference(WorkspacePositions.FolderModel, path.FolderId);
            var rows = nodes.Select(node =>
            {
                var child = new LinkObjectReference(WorkspacePositions.FolderModel, (string)node.Context["id"]!);
                var lid = ((IReadOnlyList<string>)node.Context["linkIds"]!)[^1];
                return new LinkQueryRow("workspace.content", lid, parent, child, child, new Dictionary<string, object?> { ["LID"] = lid, ["Name"] = node.Label }, node.HasChildren);
            }).ToArray();
            return Results.Ok(new LinkQueryResult(rows, rows.Length, 1, rows.Length, null, null));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    /// <summary>已知位置恢复返回同一条经完整授权的链，不寻找替代根或父路径。</summary>
    [HttpGet("content/path"), RequireFeature("tree:workspace.structure")]
    public async Task<IResult> Path([FromQuery] string spaceId, [FromQuery] string rootFolderId, [FromQuery] string linkIds, CancellationToken token)
    {
        try
        {
            var scope = new Dictionary<string, string?> { ["spaceId"] = spaceId, ["rootFolderId"] = rootFolderId, ["linkIds"] = linkIds };
            if (Request.Query.Any(q => !scope.ContainsKey(q.Key) || q.Value.Count != 1)) throw new LinkQueryException(400, "Workspace.InvalidPosition");
            return Results.Ok(await positions.ResolveFromApiAsync(Actor(), WorkspacePositions.FromScope(scope),
                new(new("workspace.content.path.api", "workspace.backend", "read"), "workspace.content.path.api", "query"), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
        {
            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    // 只从已认证主体取得账号，客户端位置不携带所有者。
    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
}
