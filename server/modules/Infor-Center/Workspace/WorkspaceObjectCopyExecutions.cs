using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;

namespace ICP.Platform.Modules.Workspace;

/// <summary>真副本的Part执行由服务端固定；Document仅由真实模型进入317前的明确过渡路径。</summary>
public static class WorkspaceObjectCopyExecutions
{
    public const string Part = "workspace.objectCopy.part";
    public const string Provider = "workspace.backend";

    /// <summary>真实服务入口固定当前阶段，客户端不能指定执行或提供允许事实。</summary>
    public static DataExecutionBindingReference Binding(string stage) => new(Part, Provider, stage);

    // 声明存在性只是HTTP来源形状，不是允许结果；真实Doc分支不能借Part或无效声明运行。
    internal static void RequireDeferredDocumentRequest(bool declarationSpecified)
    {
        if (declarationSpecified) throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
    }
}

public sealed partial class WorkspaceContents
{
    // 固定Part角色在原read/copy点逐项执行，不把先前允许带入后续阶段或事务。
    private async Task<bool> PartCopyAllowedAsync(string stage, string role, string id, CancellationToken token) =>
        await executionAccess.EvaluateObjectAsync(WorkspaceObjectCopyExecutions.Binding(stage), "part.part", role, id, token)
            == DataExecutionDecision.Allowed;

    // 原最终候选复核固定实际调用者；创建后不可读不成为结果读取门槛。
    private static DataExecutionWriteContext PartCopyFinalCandidateSource() =>
        new(new("part.part.copy.finalCandidate", "standardCrud.create", "submit"), "part.part", null)
        { Internal = new("part.part.copy.finalCandidate", "standardCrud.create", typeof(WorkspaceContents), nameof(CopyObjectAsync)) };
    // 原位置完整read仍由既有resolver取得，再执行同一次父edit，不加authorize或额外查询。
    private Task<WorkspacePositionPath> ObjectCopyTargetPositionAsync(string actor, WorkspacePosition position,
        string model, string stage, CancellationToken token) => model == "Part"
            ? ResolveClipboardPositionAsync(WorkspaceObjectCopyExecutions.Binding(stage), "parent", position, token)
            : WritablePositionAsync(actor, position, token);
}