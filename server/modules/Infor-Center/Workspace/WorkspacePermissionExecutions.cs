using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

// 单对象、批量和只读页面保留各自真实声明；辅助目录显式接受实际调用它的有限来源。
internal static class WorkspacePermissionExecutions
{
    internal static readonly string[] Resources = ["workspace.folder", "part.part", "document.document"];
    // 单对象管理使用自身固定能力，阶段由原服务端准备或提交位置选择。
    internal static DataExecutionBindingReference Management(string stage) => new("workspace.content.authorization.manage", "authorization.collection.manage", stage);
    // 手工批量管理保留独立执行身份，不能借单对象声明替代整个集合要求。
    internal static DataExecutionBindingReference Batch(string stage) => new("workspace.content.authorization.batch", "authorization.collection.manage", stage);
    internal static readonly DataExecutionBindingReference[] ManagementSources = [Management("prepare"), Batch("prepare")];
    internal static readonly DataExecutionBindingReference[] ViewSources =
        [new("workspace.content.authorization.view", "authorization.collection.view", "prepare"), .. ManagementSources];
}

public sealed partial class WorkspaceContents
{
    // 完整位置或筛选可以合法没有结果，但声明和功能来源必须在遍历之前成立。
    internal void ValidatePermissionSources(IReadOnlyList<DataExecutionBindingReference> sources)
    {
        foreach (var resource in WorkspacePermissionExecutions.Resources) executionAccess.Validate(sources, resource);
    }
}
