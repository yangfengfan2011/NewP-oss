using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;

namespace ICP.Platform.Modules.Part;

/// <summary>复用标准创建更新删除，仅在成功删除后登记所属业务事件。</summary>
public sealed class PartObjectBehavior(IStandardCrudResourceRegistry resources, StandardCrudDefaultWriter writer,
    IStandardCrudRepository repository, PartBusinessRecorder recorder) : ObjectBehaviorBase(BehaviorDefinition, resources, writer)
{
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new("part.part", "Part", "Part",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete], [],
        ["EID", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);

    /// <inheritdoc/>
    protected override async ValueTask<StandardCrudMutationResult> DeleteAsync(ObjectOperationContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TargetId)) return await base.DeleteAsync(context, cancellationToken);
        var deleted = await repository.GetByIdAsync(resources.FindByResourceKey(Definition.ObjectKey)!, context.TargetId, cancellationToken);
        var result = await base.DeleteAsync(context, cancellationToken);
        if (result.Succeeded && deleted is not null) await recorder.RegisterDeletedAsync(deleted, cancellationToken);
        return result;
    }
}
