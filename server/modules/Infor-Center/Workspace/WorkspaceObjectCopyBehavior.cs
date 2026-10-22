using System.Text.Json;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>唯一对象操作根把复制owner、关系、审计和结果登记纳入同一失败裁决。</summary>
public sealed class WorkspaceObjectCopyBehavior(Func<WorkspaceContents> contents, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    public static readonly ObjectOperationAction CopyPart = new("copyPart");
    public static readonly ObjectOperationAction CopyDocument = new("copyDocument");
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new("workspace.content.copy", "Workspace", "WorkspaceObjectCopy",
        [CopyPart, CopyDocument], [], ["ActorId", "OperationId"], [new(CopyPart, new("part.part.copy", new("copy"))), new(CopyDocument, new("document.document.copy", new("copy")))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default) =>
        contents().CopyObjectAsync((string)context.Values["ActorId"]!, (string)context.Values["OperationId"]!,
            (IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!, operations(), cancellationToken);
}
