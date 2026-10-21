using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
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
    private readonly IDataExecutionAccess _executions;
    private readonly IPlatformObjectIdGenerator? _objectIds;
    private readonly ICP.Platform.Crud.Ownership.EntityOwnershipValues? _ownership;
    private readonly ICP.Platform.Crud.Ownership.EntityOwnershipAudit? _ownershipAudit;
    private readonly IButtonExecutionAccess? _buttons;

    /// <summary>组合默认写入依赖；事务与审计仍复用平台已有 owner，不依赖查询或展示服务。</summary>
    public StandardCrudDefaultWriter(
        IDataExecutionAccess executions,
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards,
        IEnumerable<IStandardCrudConditionalDeleteExecutor>? conditionalDeleteExecutors = null,
        IEntityAuditValueProvider? auditValueProvider = null,
        ITemporalInputNormalizer? temporalInputNormalizer = null,
        IEnumerable<IStandardCrudCreationRecorder>? creationRecorders = null,
        IPlatformTransactionExecutor? transactionExecutor = null,
        IEnumerable<IStandardCrudUpdateRecorder>? updateRecorders = null,
        IPlatformObjectIdGenerator? objectIds = null,
        ICP.Platform.Crud.Ownership.EntityOwnershipValues? ownership = null,
        ICP.Platform.Crud.Ownership.EntityOwnershipAudit? ownershipAudit = null,
        IButtonExecutionAccess? buttons = null)
    {
        _repository = repository;
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _conditionalDeleteExecutors = (conditionalDeleteExecutors ?? [])
            .ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _creationRecorders = (creationRecorders ?? []).ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _updateRecorders = (updateRecorders ?? []).ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _transactionExecutor = transactionExecutor;
        _rules = new StandardCrudWriteRules(repository, validators, temporalInputNormalizer, auditValueProvider);
        _executions = executions ?? throw new ArgumentNullException(nameof(executions));
        _objectIds = objectIds;
        _ownership = ownership;
        _ownershipAudit = ownershipAudit;
        _buttons = buttons;
    }

    // 来源不进入业务值，正式裁决仍处于原事务和事实保护位置。
    internal async ValueTask<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        ObjectOperationWriteSource? source, CancellationToken cancellationToken)
    {
        if (source is ObjectOperationWriteSource.Button && _transactionExecutor is null)
            throw new InvalidOperationException("BUTTON_PROTECTED_CREATE_REQUIRED");
        if (_transactionExecutor is { IsActive: false } && (source is ObjectOperationWriteSource.Button || _transactionExecutor.HasParticipants(descriptor.ModelName)))
            return await ExecuteCoordinatedAsync(
                descriptor,
                token => CreateAsync(descriptor, values, source, token).AsTask(),
                cancellationToken);
        AssertWriteSource(descriptor, "create", null, source);
        var preparation = source is ObjectOperationWriteSource.Button or ObjectOperationWriteSource.CreateContinuation
            ? await PrepareCreateCandidateCoreAsync(descriptor, values, cancellationToken)
            : await PrepareCreateCandidateAsync(descriptor, values, source, cancellationToken);
        if (!preparation.Succeeded) return preparation;
        var candidate = preparation.Detail!.Fields;
        if (source is ObjectOperationWriteSource.CreateContinuation continuation)
            return await continuation.InvokeAsync(candidate, (official, token) =>
            {
                AssertWriteSource(descriptor, "create", null, source);
                return CreateAuthorizedCoreAsync(descriptor, official, token);
            }, cancellationToken);
        return source is ObjectOperationWriteSource.Button button
            ? await (_buttons ?? throw new InvalidOperationException("BUTTON_EXECUTION_REQUIRED")).ExecuteAsync(button.Context,
                new ButtonCreationTarget(descriptor.ResourceKey, descriptor.ModelName, candidate),
                token => CreateAuthorizedCoreAsync(descriptor, candidate, token), cancellationToken)
            : await CreateAuthorizedCoreAsync(descriptor, candidate, cancellationToken);
    }

    // 唯一规范候选在自动检查后进入原持久化与记录器；不重新补字段或替换候选身份。
    private async Task<StandardCrudMutationResult> CreateAuthorizedCoreAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> allowed, CancellationToken cancellationToken)
    {
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
        if (allowed.ContainsKey("OwnerId") || allowed.ContainsKey("OwnerDepartmentId"))
        {
            if (_ownershipAudit is null) throw new InvalidOperationException("ENTITY_OWNERSHIP_AUDIT_REQUIRED");
            await _ownershipAudit.RegisterAsync(descriptor.ResourceKey, (string)created[descriptor.IdField]!, allowed, false, cancellationToken);
        }
        return StandardCrudMutationResult.Success(new StandardCrudDetail
        {
            Fields = created
        });
    }

    /// <summary>准备正式创建候选及可信归属审计；旧声明消费原create要求，显式内部来源不附加动作下限，不登记对象或开启事务。</summary>
    /// <remarks>内部候选包含本次服务端身份，仅供业务预检；不能公开它或把成功当作后续提交的凭证。</remarks>
    public async Task<StandardCrudMutationResult> PrepareCreateCandidateAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source, CancellationToken cancellationToken = default)
    {
        if (source is ObjectOperationWriteSource.CreateContinuation)
            throw new ActionDataAccessException(400, "ObjectOperation.CreationPersistenceRequired");
        AssertWriteSource(descriptor, "create", null, source);
        var preparation = await PrepareCreateCandidateCoreAsync(descriptor, values, cancellationToken);
        if (!preparation.Succeeded) return preparation;
        var candidate = preparation.Detail!.Fields;
        if (source is ObjectOperationWriteSource.Button button)
            return await (_buttons ?? throw new InvalidOperationException("BUTTON_EXECUTION_REQUIRED")).ExecuteAsync(button.Context,
                new ButtonCreationTarget(descriptor.ResourceKey, descriptor.ModelName, candidate), _ => Task.FromResult(preparation), cancellationToken);
        if (source is ObjectOperationWriteSource.Declared declared && await _executions.EvaluateCandidateAsync(declared.Context, descriptor.ResourceKey,
            "candidate", candidate, cancellationToken) != DataExecutionDecision.Allowed)
            return StandardCrudMutationResult.Failure(ScopeDenied(descriptor));
        return preparation;
    }

    /// <summary>仅准备完整创建事实供业务自己的候选检查使用，不判权、登记实体或取得写事务。</summary>
    /// <remarks>结果包含本次服务端新身份；成功不是写入来源或后续提交允许，实际创建仍须在正式边界重新准备和检查。</remarks>
    public Task<StandardCrudMutationResult> PrepareCreateCandidateFactsAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
        PrepareCreateCandidateCoreAsync(descriptor, values, cancellationToken);

    // 业务字段、服务端默认值、可信审计、归属和真实新EID只在这里补齐，不携带权限允许状态。
    private async Task<StandardCrudMutationResult> PrepareCreateCandidateCoreAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var preparation = await _rules.PrepareCreateAsync(descriptor, values, cancellationToken);
        if (!preparation.Succeeded) return StandardCrudMutationResult.Failure(preparation.Error!);
        var ownedCandidate = new Dictionary<string, object?>(preparation.Values!, StringComparer.Ordinal);
        var ownershipFields = descriptor.FieldPolicies.Where(policy => ICP.Platform.Modeling.Ownership.EntityOwnershipFields.Contains(policy.Field)).Select(policy => policy.Field).ToArray();
        if (ownershipFields.Length > 0)
        {
            if (_ownership is null) throw new InvalidOperationException("ENTITY_OWNERSHIP_PROVIDER_REQUIRED");
            await _ownership.ApplyCreateAsync(ownedCandidate, ownershipFields, cancellationToken);
        }
        if (_objectIds is null) throw new InvalidOperationException("DATA_SCOPE_OBJECT_ID_GENERATOR_REQUIRED");
        IReadOnlyDictionary<string, object?> candidate = new Dictionary<string, object?>(ownedCandidate, StringComparer.Ordinal)
        {
            [descriptor.IdField] = _objectIds.NewId(descriptor.ModelName)
        };
        return StandardCrudMutationResult.Success(new StandardCrudDetail { Fields = candidate });
    }

    // 来源不进入业务值，正式裁决仍处于原事务和事实保护位置。
    internal async ValueTask<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        ObjectOperationWriteSource? source, CancellationToken cancellationToken)
    {
        if (_transactionExecutor is { IsActive: false } && _transactionExecutor.HasParticipants(descriptor.ModelName))
            return await ExecuteCoordinatedAsync(
                descriptor,
                token => UpdateAsync(descriptor, id, values, source, token).AsTask(),
                cancellationToken);
        StandardCrudDescriptorValidator.Validate(descriptor);
        if (!descriptor.RequiresUpdateRecord)
        {
            if (source is not ObjectOperationWriteSource.Button)
                return await UpdateCoreAsync(descriptor, id, values, null, null, source, cancellationToken);
            if (_transactionExecutor is null || _repository is not IStandardCrudUpdateParticipant protectedUpdate)
                throw new InvalidOperationException("BUTTON_PROTECTED_UPDATE_REQUIRED");
            return _transactionExecutor.IsActive
                ? await UpdateCoreAsync(descriptor, id, values, protectedUpdate, null, source, cancellationToken)
                : await _transactionExecutor.ExecuteAsync(token => new ValueTask<StandardCrudMutationResult>(
                    UpdateCoreAsync(descriptor, id, values, protectedUpdate, null, source, token)), cancellationToken);
        }
        if (!_updateRecorders.TryGetValue(descriptor.ResourceKey, out var recorder)
            || _transactionExecutor is null || _repository is not IStandardCrudUpdateParticipant participant
            || !string.IsNullOrWhiteSpace(descriptor.ConcurrencyRevisionField))
            throw new InvalidOperationException("Required protected update registration is unavailable.");
        try
        {
            if (_transactionExecutor.IsActive)
            {
                return await UpdateCoreAsync(descriptor, id, values, participant, recorder, source, cancellationToken);
            }

            return await _transactionExecutor.ExecuteAsync(token => new ValueTask<StandardCrudMutationResult>(
                UpdateCoreAsync(descriptor, id, values, participant, recorder, source, token)), cancellationToken);
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
        ObjectOperationWriteSource? source, CancellationToken cancellationToken)
    {
        AssertWriteSource(descriptor, "update", id, source);
        var existing = participant is null
            ? await _repository.GetByIdAsync(descriptor, id, cancellationToken)
            : await participant.ReadForUpdateAsync(descriptor, id, cancellationToken);
        if (existing is null)
        {
            return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
        }

        if (source is ObjectOperationWriteSource.Button button)
        {
            if (values.TryGetValue(descriptor.IdField, out var suppliedId) && suppliedId?.ToString() != id)
                throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
            return await (_buttons ?? throw new InvalidOperationException("BUTTON_EXECUTION_REQUIRED")).ExecuteAsync(button.Context,
                new ButtonExecutionTarget(descriptor.ResourceKey, descriptor.ModelName, id, existing),
                token => UpdateAuthorizedCoreAsync(descriptor, id, values, existing, participant, recorder, token), cancellationToken);
        }
        var scopeError = await CheckExistingScopeAsync(descriptor, id, "edit", existing, source, cancellationToken);
        if (scopeError is not null) return StandardCrudMutationResult.Failure(scopeError);

        return await UpdateAuthorizedCoreAsync(descriptor, id, values, existing, participant, recorder, cancellationToken);
    }

    // 正式目标在外层固定；本方法只更新同一已读取对象，字段不能替换路由身份。
    private async Task<StandardCrudMutationResult> UpdateAuthorizedCoreAsync(StandardCrudDescriptor descriptor, string id,
        IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?> existing,
        IStandardCrudUpdateParticipant? participant, IStandardCrudUpdateRecorder? recorder, CancellationToken cancellationToken)
    {
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
                if (recorder is not null) await recorder.RegisterAsync(existing, updated, cancellationToken);
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
    // 来源不进入业务值，正式裁决仍处于原事务和事实保护位置。
    internal async ValueTask<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor,
        string id,
        ObjectOperationWriteSource? source, CancellationToken cancellationToken)
    {
        if (_transactionExecutor is { IsActive: false } && (source is ObjectOperationWriteSource.Button || _transactionExecutor.HasParticipants(descriptor.ModelName)))
            return await ExecuteCoordinatedAsync(descriptor, token => DeleteAsync(descriptor, id, source, token).AsTask(), cancellationToken);
        StandardCrudDescriptorValidator.Validate(descriptor);
        AssertWriteSource(descriptor, "delete", id, source);
        if (source is ObjectOperationWriteSource.Button button)
        {
            if (_transactionExecutor?.IsActive != true || _repository is not IStandardCrudUpdateParticipant participant)
                throw new InvalidOperationException("BUTTON_PROTECTED_DELETE_REQUIRED");
            var before = await participant.ReadForUpdateAsync(descriptor, id, cancellationToken);
            if (before is null) return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
            return await (_buttons ?? throw new InvalidOperationException("BUTTON_EXECUTION_REQUIRED")).ExecuteAsync(button.Context,
                new ButtonExecutionTarget(descriptor.ResourceKey, descriptor.ModelName, id, before),
                token => DeleteAuthorizedCoreAsync(descriptor, id, token), cancellationToken);
        }
        var existing = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (existing is null) return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
        var scopeError = await CheckExistingScopeAsync(descriptor, id, "delete", existing, source, cancellationToken);
        return scopeError is not null ? StandardCrudMutationResult.Failure(scopeError) : await DeleteAuthorizedCoreAsync(descriptor, id, cancellationToken);
    }

    // 当前实际目标已经完成原事实判权；条件删除、模块守卫及审计/cache事务继续由原owner执行。
    private async Task<StandardCrudMutationResult> DeleteAuthorizedCoreAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken)
    {
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
        string action, IReadOnlyDictionary<string, object?> existing, ObjectOperationWriteSource? source, CancellationToken token)
    {
        if (source is ObjectOperationWriteSource.Internal) return null;
        var declared = (ObjectOperationWriteSource.Declared)source!;
        var result = await _executions.EvaluateObjectAsync(declared.Context, "target",
            new DataExecutionWriteFacts(descriptor.ResourceKey, id, existing), token);
        return result == DataExecutionDecision.Allowed ? null : result == DataExecutionDecision.NotFound ? NotFound(descriptor, id) : ScopeDenied(descriptor);
    }

    // 来源只能是本次正式写；启停复用update核心，但随后仍按其原入口重新绑定并检查同一目标。
    private void AssertWriteSource(StandardCrudDescriptor descriptor, string operation, string? id, ObjectOperationWriteSource? source)
    {
        if (source is ObjectOperationWriteSource.CreateContinuation continuation)
        {
            if (_transactionExecutor?.IsActive != true || operation != "create" || id is not null)
                throw new ActionDataAccessException(400, "ObjectOperation.CreationContextRequired");
            continuation.Validate(descriptor.ResourceKey, descriptor.ModelName);
            return;
        }
        if (source is ObjectOperationWriteSource.Internal internalSource)
        {
            if (_transactionExecutor?.IsActive != true) throw new ActionDataAccessException(400, "ObjectOperation.InternalContextRequired");
            if (internalSource.ResourceKey != descriptor.ResourceKey || internalSource.ObjectName != descriptor.ModelName
                || internalSource.Action?.Value != operation || internalSource.ObjectId != id
                || id is not null && !id.StartsWith(descriptor.ModelName + "_", StringComparison.Ordinal))
                throw new ActionDataAccessException(400, "ObjectOperation.InternalBindingInvalid");
            return;
        }
        if (source is ObjectOperationWriteSource.Button button)
        {
            if (operation is not ("create" or "update" or "delete") || button.Context.ResourceKey != descriptor.ResourceKey || button.Context.ObjectName != descriptor.ModelName
                || button.Context.ObjectId != id || !(button.Context.BusinessBinding == descriptor.ResourceKey + "." + operation
                    || operation == "update" && (button.Context.BusinessBinding == descriptor.ResourceKey + ".enable"
                        || button.Context.BusinessBinding == descriptor.ResourceKey + ".disable"))
                || descriptor.DataExecutions.ContainsKey(operation))
                throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
            return;
        }
        if (source is not ObjectOperationWriteSource.Declared declared || declared.Context is null)
            throw new ActionDataAccessException(400, "ActionDataAccess.WriteContextRequired");
        var context = declared.Context;
        if (context.ResourceKey != descriptor.ResourceKey || context.ObjectId != id
            || context.Binding.Provider != "standardCrud." + operation || context.Binding.Stage != "submit"
            || context.Internal is null && (!descriptor.DataExecutions.TryGetValue(operation, out var key) || context.Binding.Execution != key))
            throw new ActionDataAccessException(400, "ActionDataAccess.WriteBindingInvalid");
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
