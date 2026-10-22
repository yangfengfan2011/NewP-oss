using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

/// <summary>权限来源的完整read边界：原Link端点404之后仍独立判data-read403，不信上层已允许或增加所属对象读取。</summary>
public sealed class WorkspacePermissionTargetReadResolver(LinkQueryReader reader, IDataScopeObjectDecisionProvider decisions) : IDataExecutionTargetResolver
{
    public const string Execution = "workspace.permission.targets.read";
    public string Key => "workspace.permission.target.fullRead";
    public string ModuleId => "Workspace";

    /// <inheritdoc/>
    public async Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
    {
        if (target.Source is not LinkObjectReference reference || target.ObjectId != reference.Id
            || target.ResourceKey != (reference.ModelKey switch
            {
                "WorkspaceFolder" => "workspace.folder", "Part" => "part.part", "Document" => "document.document",
                _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
            })) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        if (!await reader.IsEndpointReadableAsync("workspace.content", reference, token)) return null;
        if (!await decisions.IsAllowedAsync(target.ResourceKey, reference.Id, "read", token))
            throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
        return new(target.ResourceKey, reference.Id, reference);
    }
}
