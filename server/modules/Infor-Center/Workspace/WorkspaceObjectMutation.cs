using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>保留对象修改的静态调用声明和平台执行上下文，完整图命令由 WorkspaceContents 拥有。</summary>
public sealed class WorkspaceObjectMutation(Func<WorkspaceContents> contents, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "workspace.content.mutation", "Workspace", WorkspacePositions.ContentModel,
        [ObjectOperationAction.Update, ObjectOperationAction.Delete], [], ["ActorId", "Position", "LinkIds"],
        [new(ObjectOperationAction.Update, new("part.part", ObjectOperationAction.Update)),
         new(ObjectOperationAction.Update, new("document.document", ObjectOperationAction.Update)),
         new(ObjectOperationAction.Delete, new("part.part", ObjectOperationAction.Delete)),
         new(ObjectOperationAction.Delete, new("document.document", ObjectOperationAction.Delete))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default) =>
        contents().MutateObjectsAsync(context, operations(), cancellationToken);
}
