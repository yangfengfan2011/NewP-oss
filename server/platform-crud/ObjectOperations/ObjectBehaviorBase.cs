using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using System.Collections.Frozen;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 提供对象动作分派的轻量基类；派生行为只实现对象实际支持的动作。
/// </summary>
public abstract class ObjectBehaviorBase(ObjectBehaviorDefinition definition) : IObjectBehavior
{
    /// <inheritdoc />
    public ObjectBehaviorDefinition Definition { get; } = definition
        ?? throw new ArgumentNullException(nameof(definition));

    /// <inheritdoc />
    public ValueTask<StandardCrudMutationResult> ExecuteAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.ObjectKey, Definition.ObjectKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_TARGET_MISMATCH: {context.ObjectKey}/{Definition.ObjectKey}");
        }

        if (!Definition.SupportedActions.Contains(context.Action))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_ACTION_UNSUPPORTED: {Definition.ObjectKey}/{context.Action.Value}");
        }

        return context.Action == ObjectOperationAction.Create
            ? CreateAsync(context, cancellationToken)
            : ExecuteDeclaredActionAsync(context, cancellationToken);
    }

    // 创建是首个真实采用动作；其余已声明动作由后续切片在同一分派点接入。
    protected virtual ValueTask<StandardCrudMutationResult> CreateAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"OBJECT_OPERATION_ACTION_UNIMPLEMENTED: {Definition.ObjectKey}/{context.Action.Value}");

    // 专用动作保持明确名称，不把所有业务写入压成 Save。
    protected virtual ValueTask<StandardCrudMutationResult> ExecuteDeclaredActionAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"OBJECT_OPERATION_ACTION_UNIMPLEMENTED: {Definition.ObjectKey}/{context.Action.Value}");

    /// <summary>
    /// 执行动态 Entity 的默认创建：收敛最终输入、登记实体并在同一事务登记业务事实。
    /// </summary>
    protected static async ValueTask<StandardCrudMutationResult> ExecuteDefaultCreateAsync(
        ObjectOperationContext context,
        StandardCrudDescriptor descriptor,
        StandardCrudCreateRequestPreparer preparer,
        IStandardCrudCreationParticipant participant,
        IStandardCrudCreationRecorder? recorder,
        CancellationToken cancellationToken)
    {
        var preparation = await preparer.PrepareAsync(descriptor, context.Values, cancellationToken);
        if (!preparation.Succeeded)
        {
            return StandardCrudMutationResult.Failure(preparation.Error!);
        }

        var created = participant.RegisterCreate(descriptor, preparation.Values!);
        if (recorder is not null)
        {
            await recorder.RegisterAsync(created, cancellationToken);
        }

        return StandardCrudMutationResult.Success(new StandardCrudDetail
        {
            Fields = created.ToFrozenDictionary(StringComparer.Ordinal)
        });
    }
}
