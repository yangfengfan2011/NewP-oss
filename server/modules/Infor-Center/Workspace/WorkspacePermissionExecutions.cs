using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

// 目录和只读页面共用原view来源；管理按钮和其保存不再依赖旧执行合同。
internal static class WorkspacePermissionExecutions
{
    internal static readonly string[] Resources = ["workspace.folder", "part.part", "document.document"];
    internal static readonly DataExecutionBindingReference[] ViewSources =
        [new("workspace.content.authorization.view", "authorization.collection.view", "prepare")];
}

public sealed partial class WorkspaceContents
{
    // 完整位置或筛选可以合法没有结果，但声明和功能来源必须在遍历之前成立。
    internal void ValidatePermissionSources(IReadOnlyList<DataExecutionBindingReference> sources)
    {
        foreach (var resource in WorkspacePermissionExecutions.Resources) executionAccess.Validate(sources, resource);
    }
}
