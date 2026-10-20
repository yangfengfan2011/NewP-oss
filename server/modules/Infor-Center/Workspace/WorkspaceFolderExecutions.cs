using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>文件夹基础入口与真实目标阶段的固定执行身份。</summary>
public static class WorkspaceFolderExecutions
{
    public const string Create = "workspace.folder.create";
    public const string Candidates = "workspace.folder.candidates";
    public const string Provider = "workspace.backend";

    /// <summary>当前候选只有公开HTTP消费者，固定API绑定不借树或内容view声明。</summary>
    public static DataExecutionApiQueryContext CandidatesQuery() => new(new(Candidates, Provider, "read"), Candidates, "query");
}
