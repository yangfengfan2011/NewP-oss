using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 人员对象的正式创建行为；输入规则、默认写入和业务审计在同一对象操作内完成。
/// </summary>
public sealed class PersonObjectBehavior(
    IStandardCrudResourceRegistry resources,
    StandardCrudCreateRequestPreparer preparer,
    IStandardCrudRepository repository,
    PersonCreationRecorder recorder)
    : ObjectBehaviorBase(BehaviorDefinition)
{
    /// <summary>人员首片只实现并开放创建动作。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.person",
        "Organization",
        "Organization/Person",
        [ObjectOperationAction.Create],
        [ObjectOperationAction.Create],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);

    // 最终业务输入在行为内经过标准规则与 Person validator 后才登记写入。
    protected override ValueTask<StandardCrudMutationResult> CreateAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken)
    {
        var descriptor = resources.FindByResourceKey(Definition.ObjectKey)
            ?? throw new InvalidOperationException("OBJECT_OPERATION_RESOURCE_NOT_FOUND: organization.person");
        var participant = repository as IStandardCrudCreationParticipant
            ?? throw new InvalidOperationException("OBJECT_OPERATION_CREATE_PARTICIPANT_REQUIRED: organization.person");
        return ExecuteDefaultCreateAsync(
            context,
            descriptor,
            preparer,
            participant,
            recorder,
            cancellationToken);
    }
}
