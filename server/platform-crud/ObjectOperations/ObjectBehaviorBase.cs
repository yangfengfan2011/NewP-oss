using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 提供对象动作分派的轻量基类；派生行为只实现对象实际支持的动作。
/// </summary>
public abstract class ObjectBehaviorBase : IObjectBehavior
{
    private readonly IStandardCrudResourceRegistry? _resources;
    private readonly Func<StandardCrudService>? _resolveDefaultOperations;

    // 专用行为只提供静态声明，并显式重写其拥有的动作。
    protected ObjectBehaviorBase(ObjectBehaviorDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    // 公共默认行为持有正式资源目录与同作用域核心解析器，不保存单次操作状态。
    protected ObjectBehaviorBase(
        ObjectBehaviorDefinition definition,
        IStandardCrudResourceRegistry resources,
        Func<StandardCrudService> resolveDefaultOperations)
        : this(definition)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _resolveDefaultOperations = resolveDefaultOperations ?? throw new ArgumentNullException(nameof(resolveDefaultOperations));
    }

    /// <inheritdoc />
    public ObjectBehaviorDefinition Definition { get; }

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

        if (context.Action == ObjectOperationAction.Create)
        {
            return CreateAsync(context, cancellationToken);
        }

        if (context.Action == ObjectOperationAction.Update)
        {
            return UpdateAsync(context, cancellationToken);
        }

        if (context.Action == ObjectOperationAction.Delete)
        {
            return DeleteAsync(context, cancellationToken);
        }

        return ExecuteDeclaredActionAsync(context, cancellationToken);
    }

    // 未采用公共默认行为的对象必须显式实现创建，避免声明动作后静默空操作。
    protected virtual ValueTask<StandardCrudMutationResult> CreateAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        ResolveDefaultOperations(context).ExecuteDefaultCreateAsync(
            ResolveDescriptor(context),
            context.Values,
            cancellationToken);

    // 未采用公共默认行为的对象必须显式实现更新，目标标识由统一入口单独保护。
    protected virtual ValueTask<StandardCrudMutationResult> UpdateAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        ResolveDefaultOperations(context).ExecuteDefaultUpdateAsync(
            ResolveDescriptor(context),
            RequireTargetId(context),
            context.Values,
            cancellationToken);

    // 未采用公共默认行为的对象必须显式实现删除，物理删除不写入审计四字段。
    protected virtual ValueTask<StandardCrudMutationResult> DeleteAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        ResolveDefaultOperations(context).ExecuteDefaultDeleteAsync(
            ResolveDescriptor(context),
            RequireTargetId(context),
            cancellationToken);

    // 专用动作保持明确名称，不把所有业务写入压成 Save。
    protected virtual ValueTask<StandardCrudMutationResult> ExecuteDeclaredActionAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"OBJECT_OPERATION_ACTION_UNIMPLEMENTED: {Definition.ObjectKey}/{context.Action.Value}");

    // 只有通过公共默认行为注册的实例拥有默认核心；专用派生类遗漏重写时保持失败关闭。
    private StandardCrudService ResolveDefaultOperations(ObjectOperationContext context) =>
        _resolveDefaultOperations?.Invoke()
        ?? throw new InvalidOperationException(
            $"OBJECT_OPERATION_ACTION_UNIMPLEMENTED: {Definition.ObjectKey}/{context.Action.Value}");

    // 默认行为只消费正式资源目录中的 descriptor，不建立第二份对象写入配置。
    private ICP.Platform.Crud.Descriptors.StandardCrudDescriptor ResolveDescriptor(ObjectOperationContext context) =>
        _resources?.FindByResourceKey(Definition.ObjectKey)
        ?? throw new InvalidOperationException($"OBJECT_OPERATION_RESOURCE_NOT_FOUND: {context.ObjectKey}");

    // 创建以外的默认动作必须携带不可变目标标识，不能从可修改业务字段推断。
    private static string RequireTargetId(ObjectOperationContext context) =>
        !string.IsNullOrWhiteSpace(context.TargetId)
            ? context.TargetId
            : throw new InvalidOperationException(
                $"OBJECT_OPERATION_TARGET_REQUIRED: {context.ObjectKey}/{context.Action.Value}");

}
