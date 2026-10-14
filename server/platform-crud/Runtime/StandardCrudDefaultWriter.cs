using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 组合默认 CRUD 写入规则、持久化及记录登记；公开类型仅供对象行为跨程序集注入。
/// 执行入口保持平台内部可见，已接入对象由统一执行器调用。
/// </summary>
public sealed class StandardCrudDefaultWriter
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudDeleteGuard> _deleteGuards;
    private readonly IReadOnlyDictionary<string, IStandardCrudConditionalDeleteExecutor> _conditionalDeleteExecutors;
    private readonly IReadOnlyDictionary<string, IStandardCrudCreationRecorder> _creationRecorders;
    private readonly IReadOnlyDictionary<string, IStandardCrudUpdateRecorder> _updateRecorders;
    private readonly IPlatformTransactionExecutor? _transactionExecutor;
    private readonly StandardCrudWriteRules _rules;
    private readonly IDataScopeObjectDecisionProvider? _dataScope;
    private readonly IPlatformObjectIdGenerator? _objectIds;

    /// <summary>组合默认写入依赖；事务与审计仍复用平台已有 owner，不依赖查询或展示服务。</summary>
    public StandardCrudDefaultWriter(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards,
        IEnumerable<IStandardCrudConditionalDeleteExecutor>? conditionalDeleteExecutors = null,
        IEntityAuditValueProvider? auditValueProvider = null,
        ITemporalInputNormalizer? temporalInputNormalizer = null,
        IEnumerable<IStandardCrudCreationRecorder>? creationRecorders = null,
        IPlatformTransactionExecutor? transactionExecutor = null,
        IEnumerable<IStandardCrudUpdateRecorder>? updateRecorders = null,
        IDataScopeObjectDecisionProvider? dataScope = null,
        IPlatformObjectIdGenerator? objectIds = null)
    {
        _repository = repository;
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _conditionalDeleteExecutors = (conditionalDeleteExecutors ?? [])
            .ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _creationRecorders = (creationRecorders ?? []).ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _updateRecorders = (updateRecorders ?? []).ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _transactionExecutor = transactionExecutor;
        _rules = new StandardCrudWriteRules(repository, validators, temporalInputNormalizer, auditValueProvider);
        _dataScope = dataScope;
        _objectIds = objectIds;
    }

    // 默认创建核心可由统一对象行为复用；已处于对象事务时只登记变化，不再取得第二次事务所有权。
    internal async ValueTask<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (_transactionExecutor is { IsActive: false } && _transactionExecutor.HasParticipants(descriptor.ModelName))
            return await ExecuteCoordinatedAsync(
                descriptor,
                token => CreateAsync(descriptor, values, token).AsTask(),
                cancellationToken);
        var preparation = await PrepareCreateCandidateAsync(descriptor, values, cancellationToken);
        if (!preparation.Succeeded)
            return StandardCrudMutationResult.Failure(preparation.Error!);
        IReadOnlyDictionary<string, object?> allowed = preparation.Detail!.Fields;

        IReadOnlyDictionary<string, object?> created;
        try
        {
            if (descriptor.RequiresCreationRecord)
            {
                if (!_creationRecorders.TryGetValue(descriptor.ResourceKey, out var recorder)
                    || _transactionExecutor is null || _repository is not IStandardCrudCreationParticipant participant)
                    throw new InvalidOperationException("Required creation registration is unavailable.");

                if (_transactionExecutor.IsActive)
                {
                    var row = participant.RegisterCreate(descriptor, allowed);
                    await recorder.RegisterAsync(row, cancellationToken);
                    created = row;
                }
                else
                {
                    var id = await _transactionExecutor.ExecuteAsync(async token =>
                    {
                        var row = participant.RegisterCreate(descriptor, allowed);
                        await recorder.RegisterAsync(row, token);
                        return Convert.ToString(row[descriptor.IdField])!;
                    }, cancellationToken);
                    created = await _repository.GetByIdAsync(descriptor, id, cancellationToken)
                        ?? throw new InvalidOperationException("Committed resource is unavailable.");
                }
            }
            else
            {
                created = await _repository.CreateAsync(descriptor, allowed, cancellationToken);
            }
        }
        catch (StandardCrudPersistenceConflictException)
        {
            return StandardCrudMutationResult.Failure(PersistenceConflict(descriptor));
        }
        return StandardCrudMutationResult.Success(new StandardCrudDetail
        {
            Fields = created
        });
    }

    /// <summary>只读预检正式创建候选；复用字段、可信审计及数据 create 裁决，不登记对象或开启写事务。</summary>
    /// <remarks>内部候选包含本次服务端身份，仅供业务预检；不能公开它或把成功当作后续提交的凭证。</remarks>
    public async Task<StandardCrudMutationResult> PrepareCreateCandidateAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var preparation = await _rules.PrepareCreateAsync(descriptor, values, cancellationToken);
        if (!preparation.Succeeded) return StandardCrudMutationResult.Failure(preparation.Error!);
        IReadOnlyDictionary<string, object?> candidate = preparation.Values!;
        if (_dataScope is not null)
        {
            if (_objectIds is null) throw new InvalidOperationException("DATA_SCOPE_OBJECT_ID_GENERATOR_REQUIRED");
            candidate = new Dictionary<string, object?>(candidate, StringComparer.Ordinal)
            {
                [descriptor.IdField] = _objectIds.NewId(descriptor.ModelName)
            };
            if (!await _dataScope.IsCandidateAllowedAsync(descriptor.ResourceKey, candidate, "create", cancellationToken))
                return StandardCrudMutationResult.Failure(ScopeDenied(descriptor));
        }
        return StandardCrudMutationResult.Success(new StandardCrudDetail { Fields = candidate });
    }

    // 默认更新核心在对象事务内直接保护旧值、校验最终输入并登记业务审计，避免形成第二个事务 owner。
    internal async ValueTask<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (_transactionExecutor is { IsActive: false } && _transactionExecutor.HasParticipants(descriptor.ModelName))
            return await ExecuteCoordinatedAsync(
                descriptor,
                token => UpdateAsync(descriptor, id, values, token).AsTask(),
                cancellationToken);
        StandardCrudDescriptorValidator.Validate(descriptor);
        if (!descriptor.RequiresUpdateRecord)
            return await UpdateCoreAsync(descriptor, id, values, null, null, cancellationToken);
        if (!_updateRecorders.TryGetValue(descriptor.ResourceKey, out var recorder)
            || _transactionExecutor is null || _repository is not IStandardCrudUpdateParticipant participant
            || !string.IsNullOrWhiteSpace(descriptor.ConcurrencyRevisionField))
            throw new InvalidOperationException("Required protected update registration is unavailable.");
        try
        {
            if (_transactionExecutor.IsActive)
            {
                return await UpdateCoreAsync(descriptor, id, values, participant, recorder, cancellationToken);
            }

            return await _transactionExecutor.ExecuteAsync(token => new ValueTask<StandardCrudMutationResult>(
                UpdateCoreAsync(descriptor, id, values, participant, recorder, token)), cancellationToken);
        }
        catch (StandardCrudPersistenceConflictException)
        {
            return StandardCrudMutationResult.Failure(PersistenceConflict(descriptor));
        }
    }

    // 复用原更新校验顺序；参与者存在时，旧值读取、校验和规范新值登记均持有同一事务保护。
    private async Task<StandardCrudMutationResult> UpdateCoreAsync(
        StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values,
        IStandardCrudUpdateParticipant? participant, IStandardCrudUpdateRecorder? recorder,
        CancellationToken cancellationToken)
    {
        var existing = participant is null
            ? await _repository.GetByIdAsync(descriptor, id, cancellationToken)
            : await participant.ReadForUpdateAsync(descriptor, id, cancellationToken);
        if (existing is null)
        {
            return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
        }

        var scopeError = await CheckExistingScopeAsync(descriptor, id, "edit", cancellationToken);
        if (scopeError is not null) return StandardCrudMutationResult.Failure(scopeError);

        long? expectedRevision = null;
        if (!string.IsNullOrWhiteSpace(descriptor.ConcurrencyRevisionField))
        {
            if (!TryReadRevision(values, descriptor.ConcurrencyRevisionField, out var submittedRevision))
            {
                return StandardCrudMutationResult.Failure(ConcurrencyRevisionRequired(descriptor));
            }

            if (!TryReadRevision(existing, descriptor.ConcurrencyRevisionField, out var currentRevision)
                || submittedRevision != currentRevision)
            {
                return StandardCrudMutationResult.Failure(ConcurrencyConflict(descriptor));
            }

            expectedRevision = submittedRevision;
        }

        var preparation = await _rules.PrepareUpdateAsync(descriptor, id, existing, values, cancellationToken);
        if (!preparation.Succeeded)
        {
            return StandardCrudMutationResult.Failure(preparation.Error!);
        }
        var allowed = preparation.Values!;

        IReadOnlyDictionary<string, object?>? updated;
        try
        {
            if (participant is not null)
            {
                updated = await participant.RegisterUpdateAsync(descriptor, id, allowed, cancellationToken);
                await recorder!.RegisterAsync(existing, updated, cancellationToken);
            }
            else
            {
                updated = expectedRevision.HasValue
                    ? await _repository.UpdateAsync(descriptor, id, allowed, expectedRevision.Value, cancellationToken)
                    : await _repository.UpdateAsync(descriptor, id, allowed, cancellationToken);
            }
        }
        catch (StandardCrudPersistenceConflictException) when (participant is null)
        {
            return StandardCrudMutationResult.Failure(PersistenceConflict(descriptor));
        }
        return updated is null
            ? StandardCrudMutationResult.Failure(expectedRevision.HasValue
                ? ConcurrencyConflict(descriptor)
                : NotFound(descriptor, id))
            : StandardCrudMutationResult.Success(new StandardCrudDetail
            {
                Fields = updated
            });
    }

    // 默认删除核心在统一行为事务内执行 guard 或原子条件删除，不更新即将物理删除行的审计字段。
    internal async ValueTask<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken)
    {
        if (_transactionExecutor is { IsActive: false } && _transactionExecutor.HasParticipants(descriptor.ModelName))
            return await ExecuteCoordinatedAsync(
                descriptor,
                token => DeleteAsync(descriptor, id, token).AsTask(),
                cancellationToken);
        StandardCrudDescriptorValidator.Validate(descriptor);
        if (_dataScope is not null)
        {
            var existing = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
            if (existing is null) return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
            var scopeError = await CheckExistingScopeAsync(descriptor, id, "delete", cancellationToken);
            if (scopeError is not null) return StandardCrudMutationResult.Failure(scopeError);
        }
        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.None)
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.DeleteNotSupported",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 不支持删除。",
                ReasonKey = "standardCrud.deleteNotSupported"
            });
        }

        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Auditable)
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.AuditableDeleteNotSupported",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 声明了审计删除，但当前内核尚未提供审计删除实现。",
                ReasonKey = "standardCrud.auditableDeleteNotSupported"
            });
        }

        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Conditional && !_deleteGuards.TryGetValue(descriptor.ResourceKey, out var guard))
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.DeleteGuardMissing",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 使用条件删除但未注册删除守卫。",
                ReasonKey = "standardCrud.deleteGuardMissing"
            });
        }

        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Conditional)
        {
            if (!_conditionalDeleteExecutors.TryGetValue(descriptor.ResourceKey, out var conditionalDeleteExecutor))
            {
                return StandardCrudMutationResult.Failure(new StandardCrudError
                {
                    StatusCode = 409,
                    Code = "StandardCrud.ConditionalDeleteExecutorMissing",
                    Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 使用条件删除但未注册原子条件删除执行器。",
                    ReasonKey = "standardCrud.conditionalDeleteExecutorMissing"
                });
            }

            return await conditionalDeleteExecutor.DeleteAsync(descriptor, id, cancellationToken);
        }

        if (_deleteGuards.TryGetValue(descriptor.ResourceKey, out var deleteGuard))
        {
            var guardError = await deleteGuard.CanDeleteAsync(descriptor, id, cancellationToken);
            if (guardError is not null)
            {
                return StandardCrudMutationResult.Failure(guardError);
            }
        }

        var deleted = await _repository.DeleteAsync(descriptor, id, cancellationToken);
        return deleted ? StandardCrudMutationResult.Success() : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
    }

    // 正式失败结果也必须回滚，避免事务末尾重试失败的受跟踪保存。
    private async Task<StandardCrudMutationResult> ExecuteCoordinatedAsync(StandardCrudDescriptor descriptor,
        Func<CancellationToken, Task<StandardCrudMutationResult>> write, CancellationToken cancellationToken)
    {
        try
        {
            return await _transactionExecutor!.ExecuteAsync([descriptor.ModelName], async token =>
            {
                var result = await write(token);
                return result.Succeeded ? result : throw new MutationRejectedException(result);
            }, cancellationToken);
        }
        catch (MutationRejectedException exception) { return exception.Result; }
        catch (ICP.Platform.Persistence.Transactions.PlatformCacheUnavailableException exception)
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

    private sealed class MutationRejectedException(StandardCrudMutationResult result) : Exception
    {
        public StandardCrudMutationResult Result { get; } = result;
    }

    // 已存在目标先判最终读取，再判动作；关闭动作不能吞掉仍启用的读取依赖。
    private async Task<StandardCrudError?> CheckExistingScopeAsync(StandardCrudDescriptor descriptor, string id,
        string action, CancellationToken token)
    {
        if (_dataScope is null) return null;
        if (!await _dataScope.IsAllowedAsync(descriptor.ResourceKey, id, "read", token)) return NotFound(descriptor, id);
        return await _dataScope.IsAllowedAsync(descriptor.ResourceKey, id, action, token)
            ? null : ScopeDenied(descriptor);
    }

    // 功能入口资格由端点独立检查；这里仅报告数据动作明确拒绝。
    private static StandardCrudError ScopeDenied(StandardCrudDescriptor descriptor) => new()
    {
        StatusCode = 403,
        Code = "DataScope.ActionDenied",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的数据动作未获允许。",
        ReasonKey = "dataScope.actionDenied"
    };

    // revision 可能来自 EF long、测试数值或 System.Text.Json；这里只接受可无损转换的非负整数。
    private static bool TryReadRevision(
        IReadOnlyDictionary<string, object?> values,
        string field,
        out long revision)
    {
        revision = default;
        if (!values.TryGetValue(field, out var value))
        {
            return false;
        }

        var parsed = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            decimal decimalValue
                when decimalValue >= 0
                    && decimalValue <= long.MaxValue
                    && decimal.Truncate(decimalValue) == decimalValue => (long)decimalValue,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } element
                when element.TryGetInt64(out var jsonValue) => jsonValue,
            string text when long.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var textValue) => textValue,
            _ => -1
        };
        if (parsed < 0)
        {
            return false;
        }

        revision = parsed;
        return true;
    }

    // 缺少客户端已读取版本时不得降级为无条件覆盖，调用方需要重新加载详情后再提交。
    private static StandardCrudError ConcurrencyRevisionRequired(StandardCrudDescriptor descriptor) => new()
    {
        StatusCode = 409,
        Code = "StandardCrud.ConcurrencyRevisionRequired",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 缺少有效并发版本，请重新加载后再编辑。",
        ReasonKey = "standardCrud.concurrencyRevisionRequired"
    };

    // 条件更新未命中时统一返回稳定冲突，不泄漏或覆盖服务器上的新版本。
    private static StandardCrudError ConcurrencyConflict(StandardCrudDescriptor descriptor) => new()
    {
        StatusCode = 409,
        Code = "StandardCrud.ConcurrencyConflict",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 已发生变化，请重新加载后再编辑。",
        ReasonKey = "standardCrud.concurrencyConflict"
    };

    // 未找到目标时保持标准资源错误与原始调用标识。
    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的对象 {id} 不存在。",
        ReasonKey = "standardCrud.notFound"
    };

    /// <summary>所属行为在受控 Flush 遇到已确认数据库约束冲突时复用标准 409，不暴露 provider 细节。</summary>
    public static StandardCrudError PersistenceConflict(StandardCrudDescriptor descriptor) => new()
    {
        StatusCode = 409,
        Code = "StandardCrud.PersistenceConflict",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的关联数据已发生变化，请重新加载后重试。",
        ReasonKey = "standardCrud.persistenceConflict"
    };
}
