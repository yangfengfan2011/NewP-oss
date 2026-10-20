using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>浅副本的公开来源、真实新候选与直接内容分别复用原资格时点。</summary>
public static class WorkspaceFolderCopyExecutions
{
    public const string Shallow = "workspace.folder.shallowCopy";
    public const string Structure = "workspace.folder.structureCopy";
    public const string StructureNode = "workspace.folder.structureCopy.node";
    public const string DirectTarget = "workspace.folder.shallowCopy.directTarget";
    /// <summary>准备与正式提交只选择服务器已经登记的阶段。</summary>
    public static DataExecutionBindingReference Binding(string stage) => new(Shallow, WorkspaceFolderExecutions.Provider, stage);
    /// <summary>初始化来源为真实浅副本协调者，不借普通创建的调用者身份。</summary>
    public static DataExecutionInternalBinding Caller() => new(Shallow, WorkspaceFolderExecutions.Provider,
        typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderAsync));
    /// <summary>结构准备与提交各用当前真实入口声明。</summary>
    public static DataExecutionBindingReference StructureBinding(string stage) => new(Structure, WorkspaceFolderExecutions.Provider, stage);
    /// <summary>结构根及新图父初始化只接受实际结构协调者。</summary>
    public static DataExecutionInternalBinding StructureCaller() => new(Structure, WorkspaceFolderExecutions.Provider,
        typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderStructureAsync));
    /// <summary>原遍历到的每个Folder在原节点读取点消费同一正式内部要求。</summary>
    public static DataExecutionInternalBinding StructureNodeCaller() => new(StructureNode, WorkspaceFolderExecutions.Provider,
        typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderStructureAsync));
    /// <summary>每个直接子端点仍在原读取与copy点消费自己的资源下限。</summary>
    public static DataExecutionInternalBinding DirectTargetCaller() => new(DirectTarget, WorkspaceFolderExecutions.Provider,
        typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderAsync));
}
