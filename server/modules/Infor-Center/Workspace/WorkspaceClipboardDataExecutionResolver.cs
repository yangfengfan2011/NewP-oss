using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Workspace;

/// <summary>完整出现位置拥有父身份和读取事实，统一要求触发原路径读取一次后继续父 edit。</summary>
public sealed class WorkspacePositionDataExecutionResolver(WorkspacePositions positions, IHttpContextAccessor http) : IDataExecutionTargetResolver
{
    public string Key => "workspace.position";
    public string ModuleId => new WorkspaceModule().ModuleId;

    /// <inheritdoc/>
    public async Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
    {
        if (target.ResourceKey != "workspace.folder" || target.ObjectId is not null || target.Source is not WorkspacePosition position)
            throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        var actor = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
        var path = await positions.ResolveAsync(actor, position, token);
        return new("workspace.folder", path.FolderId, path);
    }
}

/// <summary>真实 LID 已解析出的端点继续使用原 Link 读取范围，不改用其他对象读取路径。</summary>
public sealed class WorkspaceClipboardEndpointDataExecutionResolver(LinkQueryReader reader) : IDataExecutionTargetResolver
{
    public string Key => "workspace.clipboard.endpoint";
    public string ModuleId => new WorkspaceModule().ModuleId;

    /// <inheritdoc/>
    public async Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
    {
        if (target.Source is not LinkObjectReference reference || target.ObjectId != reference.Id
            || target.ResourceKey != (reference.ModelKey switch
            {
                WorkspacePositions.FolderModel => "workspace.folder", "Part" => "part.part", "Document" => "document.document",
                _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
            })) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        return await reader.IsEndpointReadableAsync("workspace.content", reference, token)
            ? new(target.ResourceKey, reference.Id, reference) : null;
    }
}
