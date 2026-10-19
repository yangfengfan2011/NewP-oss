using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>文件夹入口固定执行及尚未迁移复制初始化的独立旧身份。</summary>
public static class WorkspaceFolderExecutions
{
    public const string Create = "workspace.folder.create";
    public const string Edit = "workspace.content.edit";
    public const string Reorder = "workspace.folder.reorder";
    public const string EditTarget = "workspace.folder.edit.target";
    public const string Candidates = "workspace.folder.candidates";
    public const string Provider = "workspace.backend";
    public const string ShallowInitialization = "workspace.folder.shallowCopy.initialize.legacy";
    public const string StructureInitialization = "workspace.folder.structureCopy.initialize.legacy";

    /// <summary>普通创建引用同一正式合同；调用者身份只来自服务端实际成员。</summary>
    public static DataExecutionBindingReference CreateBinding() => new(Create, Provider, "submit");
    /// <summary>重排入口只拥有当前父要求，所选及相邻目标继续消费原Link读取。</summary>
    public static DataExecutionBindingReference ReorderBinding() => new(Reorder, Provider, "submit");
    /// <summary>编辑入口只拥有真实共同父要求；目标Folder由所属实际内部阶段执行。</summary>
    public static DataExecutionBindingReference EditBinding(string stage) => new(Edit, Provider, stage);
    /// <summary>两个真实阶段独立形成来源，不从HTTP或前次允许结果选择内部调用者。</summary>
    public static DataExecutionInternalBinding EditTargetCaller(bool preparing) => new(EditTarget, Provider, typeof(WorkspaceContents),
        preparing ? nameof(WorkspaceContents.ReadEditValuesAsync) : nameof(WorkspaceContents.MutateObjectsAsync));
    /// <summary>内部执行按当前实际阶段选择唯一角色，不跨阶段复用读取事实。</summary>
    public static string EditTargetRole(bool preparing) => preparing ? "prepareTarget" : "submitTarget";
    /// <summary>当前候选只有公开HTTP消费者，固定API绑定不借树或内容view声明。</summary>
    public static DataExecutionApiQueryContext CandidatesQuery() => new(new(Candidates, Provider, "read"), Candidates, "query");
    /// <summary>初始化引用已有创建合同，不创建额外权限目录。</summary>
    public static DataExecutionInternalBinding CreateCaller() => new(Create, Provider, typeof(WorkspaceContents), nameof(WorkspaceContents.CreateFolderAsync));
    /// <summary>复制仍走明确旧执行身份，不能被缺声明的普通创建借用。</summary>
    public static DataExecutionInternalBinding CopyCaller(bool structure) => structure
        ? new(StructureInitialization, Provider, typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderStructureAsync))
        : new(ShallowInitialization, Provider, typeof(WorkspaceContents), nameof(WorkspaceContents.CopyFolderAsync));
}
