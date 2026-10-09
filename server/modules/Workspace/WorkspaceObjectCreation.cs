using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>声明真实对象创建与文件夹引用的协调范围；不复制目标模块的写入规则。</summary>
public sealed class WorkspaceObjectCreation(Func<WorkspaceContents> resolveContents, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    public static readonly ObjectOperationAction CreatePart = new("createPart");
    public static readonly ObjectOperationAction CreateDocument = new("createDocument");
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "workspace.content.creation", "Workspace", "Workspace/WorkspaceFolderContent",
        [CreatePart, CreateDocument], [], ["Position", "ActorId"],
        [new(CreatePart, new("part.part", ObjectOperationAction.Create)),
         new(CreateDocument, new("document.document", ObjectOperationAction.Create))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default) =>
        resolveContents().CreateObjectAsync(context, operations(), cancellationToken);
}
