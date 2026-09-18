using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 解析唯一对象行为，并让行为、保存和缓存提交共享同一事务及单次审计作用域。
/// </summary>
public sealed class ObjectOperationExecutor : IObjectOperationExecutor
{
    private readonly IReadOnlyDictionary<string, IObjectBehavior> _behaviors;
    private readonly IPlatformTransactionExecutor? _transactions;
    private readonly IEntityAuditValueProvider _auditValues;

    /// <summary>构建启动期冻结的对象行为表；有真实行为时事务执行器不可缺失。</summary>
    public ObjectOperationExecutor(
        IEnumerable<IObjectBehavior> behaviors,
        IEntityAuditValueProvider auditValues,
        IPlatformTransactionExecutor? transactions = null)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        _transactions = transactions;
        _auditValues = auditValues ?? throw new ArgumentNullException(nameof(auditValues));
        var registered = behaviors.ToArray();
        if (registered.Length > 0 && transactions is null)
        {
            throw new InvalidOperationException("OBJECT_OPERATION_TRANSACTION_EXECUTOR_REQUIRED");
        }
        foreach (var behavior in registered)
        {
            Validate(behavior.Definition);
        }

        var duplicate = registered
            .GroupBy(static behavior => behavior.Definition.ObjectKey, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"OBJECT_OPERATION_BEHAVIOR_DUPLICATED: {duplicate.Key}");
        }

        _behaviors = registered.ToDictionary(
            static behavior => behavior.Definition.ObjectKey,
            StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public bool CanExecute(string objectKey, ObjectOperationAction action) =>
        _behaviors.TryGetValue(objectKey, out var behavior)
        && behavior.Definition.SupportedActions.Contains(action);

    /// <inheritdoc />
    public async Task<StandardCrudMutationResult> ExecuteAsync(
        string objectKey,
        ObjectOperationAction action,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var context = new ObjectOperationContext(objectKey, action, values);
        if (!_behaviors.TryGetValue(context.ObjectKey, out var behavior))
        {
            throw new InvalidOperationException($"OBJECT_OPERATION_BEHAVIOR_NOT_FOUND: {context.ObjectKey}");
        }
        if (_transactions is null)
        {
            throw new InvalidOperationException("OBJECT_OPERATION_TRANSACTION_EXECUTOR_REQUIRED");
        }

        using var auditOperation = _auditValues.BeginOperation();
        try
        {
            return await _transactions.ExecuteAsync(
                [behavior.Definition.ModelKey],
                async token =>
                {
                    var result = await behavior.ExecuteAsync(context, token);
                    return result.Succeeded ? result : throw new OperationRejectedException(result);
                },
                cancellationToken);
        }
        catch (OperationRejectedException exception)
        {
            return exception.Result;
        }
        catch (PlatformCacheUnavailableException exception)
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 503,
                Code = exception.Code,
                Message = exception.Message,
                ReasonKey = exception.ReasonKey
            });
        }
    }

    // 启动期拒绝空声明和对象拥有者未支持却声称开放的动作。
    private static void Validate(ObjectBehaviorDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ModelKey);
        if (definition.SupportedActions.Count == 0)
        {
            throw new InvalidOperationException($"OBJECT_OPERATION_ACTIONS_REQUIRED: {definition.ObjectKey}");
        }

        var unsupportedOpenAction = definition.OpenExtensionActions
            .FirstOrDefault(action => !definition.SupportedActions.Contains(action));
        if (unsupportedOpenAction is not null)
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_OPEN_ACTION_UNSUPPORTED: {definition.ObjectKey}/{unsupportedOpenAction.Value}");
        }
    }

    private sealed class OperationRejectedException(StandardCrudMutationResult result) : Exception
    {
        internal StandardCrudMutationResult Result { get; } = result;
    }
}
