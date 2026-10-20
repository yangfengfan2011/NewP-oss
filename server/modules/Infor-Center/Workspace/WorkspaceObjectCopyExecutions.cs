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
    // 固定对象角色在原read/copy点逐项执行，不把先前允许带入后续阶段或事务。
    private async Task<bool> ObjectCopyAllowedAsync(string stage, string role, string model, string id, CancellationToken token, bool batch = false) =>
        await executionAccess.EvaluateObjectAsync(WorkspaceObjectCopyExecutions.Binding(stage, model, batch), Resource(model), role, id, token)
            == DataExecutionDecision.Allowed;

    // 原最终候选复核固定实际调用者；创建后不可读不成为结果读取门槛。
    private static DataExecutionWriteContext PartCopyFinalCandidateSource() =>
        new(new("part.part.copy.finalCandidate", "standardCrud.create", "submit"), "part.part", null)
        { Internal = new("part.part.copy.finalCandidate", "standardCrud.create", typeof(WorkspaceContents), nameof(CopyObjectAsync)) };
    // Document最终新候选原点以真实调用方法固定来源，不增加新对象read。
    private static DataExecutionWriteContext DocumentCopyFinalCandidateSource(string member) => new(
        new("document.document.copy.finalCandidate", "standardCrud.create", "submit"), "document.document", null)
        { Internal = new("document.document.copy.finalCandidate", "standardCrud.create", typeof(WorkspaceContents), member) };
    // 原位置完整read仍由既有resolver取得，再执行同一次父edit，不加authorize或额外查询。
    private Task<WorkspacePositionPath> ObjectCopyTargetPositionAsync(string actor, WorkspacePosition position,
        string model, string stage, CancellationToken token, bool batch = false) => ResolveClipboardPositionAsync(WorkspaceObjectCopyExecutions.Binding(stage, model, batch), "parent", position, token);
}