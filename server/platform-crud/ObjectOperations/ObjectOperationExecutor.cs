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
            new ObjectOperationContributionSet(
                behaviors.Select(static behavior =>
                    (IObjectBehaviorContribution)new ObjectBehaviorContribution(
                        behavior.Definition.ObjectKey,
                        behavior)),
                [],
                ObjectOperationAssemblyPlan.ForBehaviors(behaviors)),
            auditValues,
            transactions,
            executionContext)
    {
    }

    // DI 路径消费 Host 启动期已验证的完整组件装配计划。
    internal ObjectOperationExecutor(
        ObjectOperationContributionSet contributions,
        IEntityAuditValueProvider auditValues,
        IPlatformTransactionExecutor? transactions = null,
        ObjectOperationExecutionContext? executionContext = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        _plan = contributions.Plan;
        _auditValues = auditValues ?? throw new ArgumentNullException(nameof(auditValues));
        _transactions = transactions;
        _executionContext = executionContext ?? new ObjectOperationExecutionContext();
        _behaviors = contributions.Behaviors;
        _extensions = contributions.Extensions;
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
    public Task<StandardCrudMutationResult> ExecuteAsync(string objectKey, ObjectOperationAction action,
        string? targetId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(objectKey, action, targetId, values, null, cancellationToken);

    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> ExecuteAsync(string objectKey, ObjectOperationAction action,
        string? targetId, IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(objectKey, action, targetId, values, source, cancellationToken);

    // 来源随每次嵌套调用显式传递，不继承外层许可；事务、扩展与审计顺序保持原owner。
    private async Task<StandardCrudMutationResult> ExecuteCoreAsync(string objectKey, ObjectOperationAction action,
        string? targetId, IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source,
        CancellationToken cancellationToken)
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

                    var context = new ObjectOperationContext(objectKey, action, targetId, before.SnapshotValues(), source);
                    StandardCrudMutationResult result;
                    using (context.ActivateCore())
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
