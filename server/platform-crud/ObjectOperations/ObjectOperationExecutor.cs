using System.Collections.Frozen;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 解析启动期对象操作计划，并让前置、行为、后置、保存和缓存提交共享同一事务及审计作用域。
/// </summary>
public sealed class ObjectOperationExecutor : IObjectOperationExecutor
{
    private readonly ObjectOperationAssemblyPlan _plan;
    private readonly IReadOnlyDictionary<string, IObjectBehavior> _behaviors;
    private readonly IReadOnlyDictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension> _extensions;
    private readonly IPlatformTransactionExecutor? _transactions;
    private readonly IEntityAuditValueProvider _auditValues;
    private readonly ObjectOperationExecutionContext _executionContext;

    /// <summary>为不含组件扩展的直接消费者构建行为计划；正式模块贡献使用 DI 注册帮助器。</summary>
    public ObjectOperationExecutor(
        IEnumerable<IObjectBehavior> behaviors,
        IEntityAuditValueProvider auditValues,
        IPlatformTransactionExecutor? transactions = null)
        : this(Materialize(behaviors), auditValues, transactions, null)
    {
    }

    // 直接构造先物化行为一次，使实例表和静态计划使用完全相同的候选。
    private ObjectOperationExecutor(
        IObjectBehavior[] behaviors,
        IEntityAuditValueProvider auditValues,
        IPlatformTransactionExecutor? transactions,
        ObjectOperationExecutionContext? executionContext)
        : this(
            behaviors.Select(static behavior =>
                (IObjectBehaviorContribution)new ObjectBehaviorContribution(
                    behavior.Definition.ObjectKey,
                    behavior)),
            [],
            ObjectOperationAssemblyPlan.ForBehaviors(behaviors),
            auditValues,
            transactions,
            executionContext)
    {
    }

    // DI 路径消费 Host 启动期已验证的完整组件装配计划。
    internal ObjectOperationExecutor(
        IEnumerable<IObjectBehaviorContribution> behaviors,
        IEnumerable<IObjectOperationExtensionContribution> extensions,
        ObjectOperationAssemblyPlan plan,
        IEntityAuditValueProvider auditValues,
        IPlatformTransactionExecutor? transactions = null,
        ObjectOperationExecutionContext? executionContext = null)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        ArgumentNullException.ThrowIfNull(extensions);
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _auditValues = auditValues ?? throw new ArgumentNullException(nameof(auditValues));
        _transactions = transactions;
        _executionContext = executionContext ?? new ObjectOperationExecutionContext();
        _behaviors = IndexBehaviors(behaviors);
        _extensions = IndexExtensions(extensions);
        ValidateResolvedContributions();
        if (_plan.Behaviors.Count > 0 && transactions is null)
        {
            throw new InvalidOperationException("OBJECT_OPERATION_TRANSACTION_EXECUTOR_REQUIRED");
        }
    }

    /// <inheritdoc />
    public bool CanExecute(string objectKey, ObjectOperationAction action) =>
        _plan.Behaviors.TryGetValue(objectKey, out var behavior)
        && behavior.Definition.SupportedActions.Contains(action);

    /// <inheritdoc />
    public async Task<StandardCrudMutationResult> ExecuteAsync(
        string objectKey,
        ObjectOperationAction action,
        string? targetId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(values);
        if (!_plan.Behaviors.TryGetValue(objectKey, out var behaviorPlan))
        {
            throw new InvalidOperationException($"OBJECT_OPERATION_BEHAVIOR_NOT_FOUND: {objectKey}");
        }
        if (_transactions is null)
        {
            throw new InvalidOperationException("OBJECT_OPERATION_TRANSACTION_EXECUTOR_REQUIRED");
        }

        var operation = new ObjectOperationIdentity(objectKey, action);
        var behavior = _behaviors[objectKey];
        var extensionPlans = behaviorPlan.Extensions.GetValueOrDefault(action) ?? [];
        var coordination = behaviorPlan.Coordinations[action];
        var nested = _executionContext.IsActive;
        IDisposable? root = null;
        if (nested)
        {
            _executionContext.EnsureTargetAllowed(operation);
        }
        else
        {
            root = _executionContext.BeginRoot(coordination.CallsByOperation);
        }
        using var auditOperation = _auditValues.BeginOperation();
        try
        {
            return await _transactions.ExecuteAsync(
                coordination.ModelKeys,
                async token =>
                {
                    using var operationScope = _executionContext.EnterOperation(objectKey, action, targetId);
                    var before = new ObjectOperationBeforeContext(
                        objectKey,
                        action,
                        targetId,
                        values,
                        behaviorPlan.Definition.ProtectedInputFields);
                    foreach (var extensionPlan in extensionPlans)
                    {
                        var identity = new ObjectOperationExtensionIdentity(
                            objectKey,
                            action,
                            extensionPlan.ExtensionKey);
                        var error = await _extensions[identity]
                            .BeforeAsync(before, token);
                        if (error is not null)
                        {
                            throw new OperationRejectedException(StandardCrudMutationResult.Failure(error));
                        }
                    }

                    var context = new ObjectOperationContext(objectKey, action, targetId, before.SnapshotValues());
                    StandardCrudMutationResult result;
                    using (_executionContext.EnterCore())
                    {
                        result = await behavior.ExecuteAsync(context, token);
                    }
                    if (!result.Succeeded)
                    {
                        throw new OperationRejectedException(result);
                    }

                    // 核心成功即由唯一事务 owner 保存但不提交，让后续嵌套规则读取真实前序事实。
                    await _transactions.FlushAsync(token);
                    var snapshot = SnapshotResult(result);
                    var after = new ObjectOperationAfterContext(objectKey, action, targetId, context.Values, snapshot);
                    foreach (var extensionPlan in extensionPlans)
                    {
                        var identity = new ObjectOperationExtensionIdentity(
                            objectKey,
                            action,
                            extensionPlan.ExtensionKey);
                        await _extensions[identity].AfterAsync(after, token);
                    }
                    if (_executionContext.RejectedResult is { } rejected)
                    {
                        throw new OperationRejectedException(rejected);
                    }
                    _executionContext.ThrowFailureException();
                    return snapshot;
                },
                cancellationToken);
        }
        catch (OperationRejectedException exception)
        {
            if (nested)
            {
                _executionContext.Fail(exception.Result);
            }
            return exception.Result;
        }
        catch (PlatformCacheUnavailableException exception)
        {
            var failure = StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 503,
                Code = exception.Code,
                Message = exception.Message,
                ReasonKey = exception.ReasonKey
            });
            if (nested)
            {
                _executionContext.Fail(failure);
            }
            return failure;
        }
        catch (Exception exception)
        {
            if (nested)
            {
                _executionContext.Fail(exception);
            }
            throw;
        }
        finally
        {
            root?.Dispose();
        }
    }

    // 复制成功结果的两层字典，后置扩展和调用方均不能接触行为内部 tracked 容器。
    private static StandardCrudMutationResult SnapshotResult(StandardCrudMutationResult result)
    {
        if (result.Detail is null)
        {
            return StandardCrudMutationResult.Success();
        }
        return StandardCrudMutationResult.Success(new StandardCrudDetail
        {
            Fields = result.Detail.Fields.ToFrozenDictionary(StringComparer.Ordinal),
            Summaries = result.Detail.Summaries.ToFrozenDictionary(StringComparer.Ordinal)
        });
    }

    // 行为按对象 identity 绑定；实现类型可由多个对象声明安全复用。
    private static IReadOnlyDictionary<string, IObjectBehavior> IndexBehaviors(
        IEnumerable<IObjectBehaviorContribution> contributions)
    {
        var result = new Dictionary<string, IObjectBehavior>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            if (!result.TryAdd(contribution.ObjectKey, contribution.Behavior))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_BEHAVIOR_CONTRIBUTION_DUPLICATED: {contribution.ObjectKey}");
            }
        }
        return result;
    }

    // 扩展按目标动作和扩展键绑定；同一实现类型可贡献多个不同 identity。
    private static IReadOnlyDictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension> IndexExtensions(
        IEnumerable<IObjectOperationExtensionContribution> contributions)
    {
        var result = new Dictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension>();
        foreach (var contribution in contributions)
        {
            if (!result.TryAdd(contribution.Identity, contribution.Extension))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_EXTENSION_CONTRIBUTION_DUPLICATED: {contribution.Identity.ObjectKey}/{contribution.Identity.Action.Value}/{contribution.Identity.ExtensionKey}");
            }
        }
        return result;
    }

    // 正式实例必须与已验证 identity 精确对应，额外直接注册不能绕过组件装配。
    private void ValidateResolvedContributions()
    {
        if (!_behaviors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(_plan.Behaviors.Keys))
        {
            throw new InvalidOperationException("OBJECT_OPERATION_BEHAVIOR_CONTRIBUTION_MISMATCH");
        }
        foreach (var behavior in _plan.Behaviors)
        {
            var resolved = _behaviors[behavior.Key].Definition;
            if (!DefinitionsMatch(resolved, behavior.Value.Definition))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_BEHAVIOR_DEFINITION_MISMATCH: {behavior.Key}");
            }
        }
        var plannedExtensions = _plan.Behaviors
            .SelectMany(behavior => behavior.Value.Extensions.SelectMany(extension =>
                extension.Value.Select(plan => new ObjectOperationExtensionIdentity(
                    behavior.Key,
                    extension.Key,
                    plan.ExtensionKey))))
            .ToHashSet();
        if (!_extensions.Keys.ToHashSet().SetEquals(plannedExtensions))
        {
            throw new InvalidOperationException("OBJECT_OPERATION_EXTENSION_CONTRIBUTION_MISMATCH");
        }
    }

    // 行为实例可持有声明副本，但其 owner、协调范围、动作和保护字段必须与启动计划一致。
    private static bool DefinitionsMatch(ObjectBehaviorDefinition left, ObjectBehaviorDefinition right) =>
        string.Equals(left.ObjectKey, right.ObjectKey, StringComparison.Ordinal)
        && string.Equals(left.OwnerModuleId, right.OwnerModuleId, StringComparison.Ordinal)
        && string.Equals(left.ModelKey, right.ModelKey, StringComparison.Ordinal)
        && left.SupportedActions.ToHashSet().SetEquals(right.SupportedActions)
        && left.OpenExtensionActions.ToHashSet().SetEquals(right.OpenExtensionActions)
        && left.ProtectedInputFields.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(right.ProtectedInputFields)
        && left.Calls.ToHashSet().SetEquals(right.Calls);

    // 公开构造不得枚举同一延迟序列两次。
    private static IObjectBehavior[] Materialize(IEnumerable<IObjectBehavior> behaviors)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        return behaviors.ToArray();
    }

    private sealed class OperationRejectedException : Exception
    {
        // 仅用于把事务内稳定业务拒绝带回协议层，同时保持事务失败语义。
        internal OperationRejectedException(StandardCrudMutationResult result)
        {
            Result = result;
        }

        internal StandardCrudMutationResult Result { get; }
    }
}
