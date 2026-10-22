using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

/// <summary>现有无界面引用API固定服务器身份，不借用粘贴入口及其copy动作。</summary>
internal static class WorkspaceReferenceExecutions
{
    internal const string Add = "workspace.reference.add";
    // 只有真实add调用者选择此固定身份，不从请求头或客户端角色构造来源。
    internal static DataExecutionApiObjectExecutionContext Source() => new(new(Add, "workspace.backend", "submit"), Add);
}

public sealed partial class WorkspaceContents
{
    private enum ReferenceReadSource { Clipboard, Api }

    // 原图锁之后解析一次父路径，再在同一角色内判父edit，不添加authorize或目标写权限。
    private async Task<WorkspacePositionPath> ResolveReferenceParentAsync(WorkspacePosition position, CancellationToken token)
    {
        var result = await executionAccess.EvaluateApiObjectAsync(WorkspaceReferenceExecutions.Source(), "parent",
            new("workspace.folder", null, position), token);
        if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
        if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
        return result.Target?.Facts as WorkspacePositionPath ?? throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }

    // 只替换add原端点读取点；剪贴板在其原显式来源下继续同一图规则，不追加一次预读。
    private async Task RequireReferenceTargetAsync(LinkObjectReference target, CancellationToken token)
    {
        var resource = Resource(target.ModelKey);
        var result = await executionAccess.EvaluateApiObjectAsync(WorkspaceReferenceExecutions.Source(), "target", new(resource, target.Id, target), token);
        if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
        if (result.Target?.ResourceKey != resource || result.Target.ObjectId != target.Id)
            throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }
}
