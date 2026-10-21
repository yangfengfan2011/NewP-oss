using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    // 解析器在统一 read 要求内取得真实父身份与路径事实，后续 edit 不再重复完整读取。
    private async Task<WorkspacePositionPath> ResolveClipboardPositionAsync(DataExecutionBindingReference binding,
        string role, WorkspacePosition position, CancellationToken token)
    {
        WorkspacePositionPath? path = null;
        await foreach (var result in executionAccess.EvaluateObjectsAsync(binding, role, [new("workspace.folder", null, position)], token))
        {
            if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
            if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
            path = result.Target?.Facts as WorkspacePositionPath ?? throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        }
        return path ?? throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }
}
