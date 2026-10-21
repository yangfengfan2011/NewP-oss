using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

/// <summary>真副本按服务端真实模型消费同一声明的固定要求，不以声明缺失选择旧分支。</summary>
public static class WorkspaceObjectCopyExecutions
{
    public const string Copy = "workspace.objectCopy";
    public const string Provider = "workspace.backend";

    /// <summary>真实服务入口固定当前阶段，客户端不能指定执行或提供允许事实。</summary>
    public static DataExecutionBindingReference Binding(string stage, string model, bool batch = false) => new(Copy, Provider, stage) { Branch = model switch
    {
        "Part" when !batch => "part", "Document" => batch ? "documentBatch" : "document",
        _ => throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired")
    } };

}

public sealed partial class WorkspaceContents
{
    // 原业务关联和末次裁决主动检查当前事实，不依赖按钮合同或固定内部动作下限。
    private async Task<bool> ObjectCopyAllowedAsync(string role, string model, string id, CancellationToken token) =>
        await objectActions.CheckAsync(model, id, role == "visibility" ? ["read"] : model switch
        {
            "Part" => ["partObjectCopy"], "Document" => ["documentObjectCopy"],
            _ => throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired")
        }, token) == DataExecutionDecision.Allowed;

    // Document最终新候选原点以真实调用方法固定来源，不增加新对象read。
    private static DataExecutionWriteContext DocumentCopyFinalCandidateSource(string member) => new(
        new("document.document.copy.finalCandidate", "standardCrud.create", "submit"), "document.document", null)
        { Internal = new("document.document.copy.finalCandidate", "standardCrud.create", typeof(WorkspaceContents), member) };
    // 原位置完整read仍由既有resolver取得，再执行同一次父edit，不加authorize或额外查询。
    private Task<WorkspacePositionPath> ObjectCopyTargetPositionAsync(string actor, WorkspacePosition position,
        string model, string stage, CancellationToken token, bool batch = false) => ResolveWritableButtonPositionAsync(actor, position, token);
}