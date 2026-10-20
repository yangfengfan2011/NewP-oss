using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>文件夹基础入口与真实目标阶段的固定执行身份。</summary>
public static class WorkspaceFolderExecutions
{
    public const string Create = "workspace.folder.create";
    public const string Candidates = "workspace.folder.candidates";
    public const string Provider = "workspace.backend";

    /// <summary>普通创建引用同一正式合同；调用者身份只来自服务端实际成员。</summary>
    public static DataExecutionBindingReference CreateBinding() => new(Create, Provider, "submit");
    /// <summary>当前候选只有公开HTTP消费者，固定API绑定不借树或内容view声明。</summary>
    public static DataExecutionApiQueryContext CandidatesQuery() => new(new(Candidates, Provider, "read"), Candidates, "query");
    /// <summary>初始化引用已有创建合同，不创建额外权限目录。</summary>
    public static DataExecutionInternalBinding CreateCaller() => new(Create, Provider, typeof(WorkspaceContents), nameof(WorkspaceContents.CreateFolderAsync));
}
