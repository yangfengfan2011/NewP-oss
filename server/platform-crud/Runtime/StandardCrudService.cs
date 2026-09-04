using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Presentation;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly IStandardCrudRepository _repository;
    private readonly IPlatformQueryExecutor _queryExecutor;
    private readonly IReadOnlyDictionary<string, IStandardCrudQueryCustomizer> _queryCustomizers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDisplayValueResolver> _displayResolvers;
    private readonly IReadOnlyDictionary<(string ResourceKey, string Key), IStandardCrudSummaryProvider> _summaryProviders;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly IReadOnlyDictionary<(string ResourceKey, string ActionKey), IStandardCrudActionHandler> _actionHandlers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDeleteGuard> _deleteGuards;
    private readonly IReadOnlyDictionary<string, IStandardCrudConditionalDeleteExecutor> _conditionalDeleteExecutors;
    private readonly IEntityAuditStampProvider? _auditStampProvider;
    private readonly ITemporalInputNormalizer? _temporalInputNormalizer;
    private readonly ITemporalDisplayProjectionExecutor? _temporalDisplayProjectionExecutor;

    /// <summary>
    /// 组合标准 CRUD 的共享读写依赖；可信审计 owner 只在资源声明服务端必填审计字段时使用。
    /// </summary>
    public StandardCrudService(
        IStandardCrudRepository repository,
        IPlatformQueryExecutor queryExecutor,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudActionHandler> actionHandlers,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards,
        IEnumerable<IStandardCrudConditionalDeleteExecutor>? conditionalDeleteExecutors = null,
        IEntityAuditStampProvider? auditStampProvider = null,
        ITemporalInputNormalizer? temporalInputNormalizer = null,
        ITemporalDisplayProjectionExecutor? temporalDisplayProjectionExecutor = null)
    {
        _repository = repository;
        _queryExecutor = queryExecutor;
        _queryCustomizers = queryCustomizers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _displayResolvers = displayResolvers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _summaryProviders = summaryProviders.ToDictionary(item => (item.ResourceKey, item.Key));
        _validators = validators.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _actionHandlers = actionHandlers.ToDictionary(item => (item.ResourceKey, item.ActionKey));
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _conditionalDeleteExecutors = (conditionalDeleteExecutors ?? [])
            .ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _auditStampProvider = auditStampProvider;
        _temporalInputNormalizer = temporalInputNormalizer;
        _temporalDisplayProjectionExecutor = temporalDisplayProjectionExecutor;
    }

    /// <summary>
    /// 列表读取先校验 descriptor，再将模块查询扩展收敛为平台查询条件，最后补齐显示值。
    /// </summary>
    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var conditions = BuildConditions(descriptor, query);
        var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);
        var platformRequest = new PlatformQueryRequest(
            AddDisplayCompanionFields(query.Fields, StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor)),
            query.Page,
            query.PageSize,
            query.SortBy,
            query.SortDirection,
            conditions.Select(condition => new PlatformQueryCondition(
                condition.Field,
                condition.Operator,
                condition.Value,
                condition.GroupKey,
                condition.GroupOperator)).ToList());

        var result = await _queryExecutor.QueryAsync(queryDescriptor, platformRequest, cancellationToken);
        var rows = await ApplyDisplayValuesAsync(descriptor, result.Items, result.RequestedDisplayFields, cancellationToken);
        var projectedRows = rows.Select(row => Project(row, result.ResponseFields)).ToList();

        return new StandardCrudListResult(
            projectedRows,
            result.Total,
            result.Page,
            result.PageSize,
            result.SortBy,
            result.SortDirection);
    }

    // canonical relation 任一端被显式请求时统一补齐 raw/display；legacy display 声明保持原请求边界。
    private static IReadOnlyList<string>? AddDisplayCompanionFields(
        IReadOnlyList<string>? requestedFields,
        IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField> displayValueFields)
    {
        if (requestedFields is not { Count: > 0 })
        {
            return requestedFields;
        }

        var canonicalDisplayFields = displayValueFields
            .Where(static field => string.Equals(
                field.Field,
                string.Concat(field.SourceField, "_DisplayValue"),
                StringComparison.Ordinal))
            .ToList();
        var fields = new List<string>(requestedFields.Count + canonicalDisplayFields.Count);
        foreach (var requestedField in requestedFields)
        {
            var canonicalDisplayField = canonicalDisplayFields.FirstOrDefault(field =>
                string.Equals(field.SourceField, requestedField, StringComparison.Ordinal)
                || string.Equals(field.Field, requestedField, StringComparison.Ordinal));
            if (canonicalDisplayField is null)
            {
                if (!fields.Contains(requestedField, StringComparer.Ordinal))
                {
                    fields.Add(requestedField);
                }

                continue;
            }

            if (!fields.Contains(canonicalDisplayField.SourceField, StringComparer.Ordinal))
            {
                fields.Add(canonicalDisplayField.SourceField);
            }

            if (!fields.Contains(canonicalDisplayField.Field, StringComparer.Ordinal))
            {
                fields.Add(canonicalDisplayField.Field);
            }
        }

        return fields;
    }

    /// <summary>
    /// 详情读取只对声明的摘要 key 调用 provider；provider 缺失或失败都转为稳定状态块。
    /// </summary>
    public async Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var row = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var fields = await ApplyDisplayValuesToSingleRowAsync(
            descriptor,
            row,
            StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor),
            cancellationToken);
        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
        }

        return new StandardCrudDetail { Fields = fields, Summaries = summaries };
    }

    /// <summary>
    /// options 当前阶段保持轻量透传，但仍先做 descriptor 静态校验。
    /// </summary>
    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        return _repository.OptionsAsync(descriptor, query, cancellationToken);
    }

    /// <summary>
    /// lookup 读取先校验 descriptor，再由仓储在数据源完成筛选、排序、分页和当前值解析。
    /// </summary>
    public Task<StandardCrudLookupResult> LookupAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        return _repository.LookupAsync(descriptor, request, cancellationToken);
    }

    /// <summary>
    /// 按 descriptor 校验并创建资源；canonical display companion 只允许输出，写入前过滤业务白名单字段，
    /// 并将校验后发生的数据库约束竞争投影为稳定公共冲突后返回带详情的创建结果。
    /// </summary>
    public async Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var ownershipFieldError = FindReadOnlyOwnershipFieldError(descriptor, values, isCreate: true);
        if (ownershipFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(ownershipFieldError);
        }

        var readOnlyDisplayFieldError = FindReadOnlyDisplayFieldError(descriptor, values);
        if (readOnlyDisplayFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(readOnlyDisplayFieldError);
        }

        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.CreateFields),
            StringComparer.Ordinal);
        var temporalError = NormalizeTemporalInputs(descriptor, allowed);
        if (temporalError is not null)
        {
            return StandardCrudMutationResult.Failure(temporalError);
        }
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: true);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
        }
        ApplyCreateDefaults(descriptor, allowed);
        var fieldPolicyError = ValidateFieldPolicies(descriptor, allowed);
        if (fieldPolicyError is not null)
        {
            return StandardCrudMutationResult.Failure(fieldPolicyError);
        }

        var serverOwnedValueError = TryResolveRequiredAuditStamp(descriptor, out var auditStamp);
        if (serverOwnedValueError is not null)
        {
            return StandardCrudMutationResult.Failure(serverOwnedValueError);
        }

        ApplyCreateAuditFields(descriptor, allowed, auditStamp);
        var storageInvariantError = ValidateStorageInvariants(descriptor, allowed);
        if (storageInvariantError is not null)
        {
            return StandardCrudMutationResult.Failure(storageInvariantError);
        }

        var uniqueError = await ValidateUniqueConstraintsAsync(descriptor, allowed, excludedId: null, cancellationToken);
        if (uniqueError is not null)
        {
            return StandardCrudMutationResult.Failure(uniqueError);
        }

        if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
        {
            var error = await validator.ValidateCreateAsync(descriptor, allowed, cancellationToken);
            if (error is not null)
            {
                return StandardCrudMutationResult.Failure(error);
            }
        }

        IReadOnlyDictionary<string, object?> created;
        try
        {
            created = await _repository.CreateAsync(descriptor, allowed, cancellationToken);
        }
        catch (StandardCrudPersistenceConflictException)
        {
            return StandardCrudMutationResult.Failure(PersistenceConflict(descriptor));
        }
        return StandardCrudMutationResult.Success(await BuildDetailFromRowAsync(descriptor, created, cancellationToken));
    }

    /// <summary>
    /// 按描述符白名单更新资源；声明 revision 字段的资源必须提交详情版本并通过仓储条件更新，
    /// 校验后发生的数据库约束竞争统一投影为稳定公共冲突。
    /// </summary>
    public async Task<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var existing = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (existing is null)
        {
            return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
        }

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

        var ownershipFieldError = FindReadOnlyOwnershipFieldError(descriptor, values, isCreate: false);
        if (ownershipFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(ownershipFieldError);
        }

        var readOnlyDisplayFieldError = FindReadOnlyDisplayFieldError(descriptor, values);
        if (readOnlyDisplayFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(readOnlyDisplayFieldError);
        }

        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.UpdateFields),
            StringComparer.Ordinal);
        var temporalError = NormalizeTemporalInputs(descriptor, allowed);
        if (temporalError is not null)
        {
            return StandardCrudMutationResult.Failure(temporalError);
        }
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: false);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
        }
        var fieldPolicyError = ValidateFieldPolicies(descriptor, allowed);
        if (fieldPolicyError is not null)
        {
            return StandardCrudMutationResult.Failure(fieldPolicyError);
        }

        var serverOwnedValueError = TryResolveRequiredAuditStamp(descriptor, out var auditStamp);
        if (serverOwnedValueError is not null)
        {
            return StandardCrudMutationResult.Failure(serverOwnedValueError);
        }

        ApplyUpdateAuditFields(descriptor, allowed, auditStamp);
        var candidate = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
        foreach (var pair in allowed)
        {
            candidate[pair.Key] = pair.Value;
        }

        var storageInvariantError = ValidateStorageInvariants(descriptor, candidate);
        if (storageInvariantError is not null)
        {
            return StandardCrudMutationResult.Failure(storageInvariantError);
        }

        var uniqueError = await ValidateUniqueConstraintsAsync(descriptor, candidate, excludedId: id, cancellationToken);
        if (uniqueError is not null)
        {
            return StandardCrudMutationResult.Failure(uniqueError);
        }

        if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
        {
            var error = await validator.ValidateUpdateAsync(descriptor, id, allowed, cancellationToken);
            if (error is not null)
            {
                return StandardCrudMutationResult.Failure(error);
            }
        }

        IReadOnlyDictionary<string, object?>? updated;
        try
        {
            updated = expectedRevision.HasValue
                ? await _repository.UpdateAsync(descriptor, id, allowed, expectedRevision.Value, cancellationToken)
                : await _repository.UpdateAsync(descriptor, id, allowed, cancellationToken);
        }
        catch (StandardCrudPersistenceConflictException)
        {
            return StandardCrudMutationResult.Failure(PersistenceConflict(descriptor));
        }
        return updated is null
            ? StandardCrudMutationResult.Failure(expectedRevision.HasValue
                ? ConcurrencyConflict(descriptor)
                : NotFound(descriptor, id))
            : StandardCrudMutationResult.Success(await BuildDetailFromRowAsync(descriptor, updated, cancellationToken));
    }

    public async Task<StandardCrudMutationResult> DisableAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.EnabledField))
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.EnabledFieldMissing",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 未声明启停字段。",
                ReasonKey = "standardCrud.enabledFieldMissing"
            });
        }

        if (!descriptor.FieldSet.UpdateFields.Contains(descriptor.EnabledField, StringComparer.Ordinal))
        {
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.EnabledFieldNotWritable",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的启停字段未纳入更新字段白名单。",
                ReasonKey = "standardCrud.enabledFieldNotWritable"
            });
        }

        return await UpdateAsync(descriptor, id, new Dictionary<string, object?> { [descriptor.EnabledField] = false }, cancellationToken);
    }

    public async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
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

    // 查询扩展点只产出标准条件；列表真正执行统一收口到平台查询内核，避免模块逻辑直接进入持久化实现。
    private IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        return _queryCustomizers.TryGetValue(descriptor.ResourceKey, out var customizer)
            ? customizer.BuildConditions(descriptor, query)
            : [];
    }

    // 显示值补齐以 row copy 方式合并，并拒绝 resolver 覆盖原始字段、返回未请求字段或非字符串伴随值。
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApplyDisplayValuesAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken)
    {
        var values = rows
            .Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(row, StringComparer.Ordinal))
            .ToList();
        if (requestedDisplayFields.Count == 0)
        {
            return values;
        }

        var temporalPlans = descriptor.DisplayProjectionPlan?.Fields
            .Where(plan => requestedDisplayFields.Any(field => string.Equals(field.Field, plan.CompanionField, StringComparison.Ordinal)))
            .Where(static plan => plan is TemporalFieldDisplayProjectionPlan or CalendarDateFieldDisplayProjectionPlan)
            .ToDictionary(static plan => plan.CompanionField, StringComparer.Ordinal)
            ?? new Dictionary<string, FieldDisplayProjectionPlan>(StringComparer.Ordinal);
        var lookupFields = requestedDisplayFields
            .Where(field => !temporalPlans.ContainsKey(field.Field))
            .ToArray();

        IStandardCrudDisplayValueResolver? resolver = null;
        if (lookupFields.Length > 0 && !_displayResolvers.TryGetValue(descriptor.ResourceKey, out resolver))
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {descriptor.ResourceKey} 请求了显示值字段，但未注册显示值解析器。");
        }

        if (resolver is not null)
        {
            EnsureDisplayResolverImplementation(descriptor, resolver);
        }

        if (temporalPlans.Count > 0 && _temporalDisplayProjectionExecutor is null)
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {descriptor.ResourceKey} 请求了 temporal 显示值字段，但未注册平台执行器。");
        }

        if (values.Count == 0)
        {
            return values;
        }

        var resolvedRows = resolver is null
            ? rows.Select(static _ => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>()).ToArray()
            : await resolver.ResolveListAsync(descriptor, rows, lookupFields, cancellationToken);
        if (resolvedRows.Count != values.Count)
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的显示值解析器返回行数 {resolvedRows.Count}，预期 {values.Count}。");
        }

        var allowedDisplayFields = requestedDisplayFields
            .Select(static field => field.Field)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            foreach (var pair in resolvedRows[index])
            {
                if (rows[index].ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {descriptor.ResourceKey} 的显示值解析器尝试覆盖原始字段 {pair.Key}。");
                }

                if (!allowedDisplayFields.Contains(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {descriptor.ResourceKey} 的显示值解析器返回未请求字段 {pair.Key}。");
                }

                if (pair.Value is not null and not string)
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {descriptor.ResourceKey} 的显示值解析器返回字段 {pair.Key} 的值必须是 string 或 null。");
                }

                ((Dictionary<string, object?>)values[index])[pair.Key] = pair.Value;
            }

            foreach (var (companionField, plan) in temporalPlans)
            {
                var rawValue = rows[index].TryGetValue(plan.SourceField, out var value) ? value : null;
                object? displayValue = (plan, rawValue) switch
                {
                    (_, null) => null,
                    (TemporalFieldDisplayProjectionPlan instantPlan, DateTime instant) =>
                        _temporalDisplayProjectionExecutor!.Project(instantPlan, instant).DisplayValue,
                    (CalendarDateFieldDisplayProjectionPlan datePlan, DateOnly date) =>
                        _temporalDisplayProjectionExecutor!.Project(datePlan, date).DisplayValue,
                    _ => throw new InvalidOperationException(
                        $"标准 CRUD 资源 {descriptor.ResourceKey} 的 temporal 字段 {plan.SourceField} 与计划类型不匹配。")
                };
                ((Dictionary<string, object?>)values[index])[companionField] = displayValue;
            }
        }

        return values;
    }

    private static void EnsureDisplayResolverImplementation(
        StandardCrudDescriptor descriptor,
        IStandardCrudDisplayValueResolver resolver)
    {
        var resolverType = resolver.GetType();
        var implementsBatch = resolverType.GetMethod(
            nameof(IStandardCrudDisplayValueResolver.ResolveListAsync),
            [typeof(StandardCrudDescriptor), typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>), typeof(IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField>), typeof(CancellationToken)]) is not null;
        var implementsSingle = resolverType.GetMethod(
            nameof(IStandardCrudDisplayValueResolver.ResolveAsync),
            [typeof(StandardCrudDescriptor), typeof(IReadOnlyDictionary<string, object?>), typeof(CancellationToken)]) is not null;
        if (!implementsBatch && !implementsSingle)
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的显示值解析器未实现批量 ResolveListAsync，也未提供兼容的单行 ResolveAsync。");
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>> ApplyDisplayValuesToSingleRowAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken)
    {
        var resolvedRows = await ApplyDisplayValuesAsync(descriptor, [row], requestedDisplayFields, cancellationToken);
        return resolvedRows[0];
    }

    // 写入后统一按 detail 流程补齐显示值和摘要块，保证 create/update/disable 的返回结构与详情读取一致。
    private async Task<StandardCrudDetail> BuildDetailFromRowAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken)
    {
        var fields = await ApplyDisplayValuesToSingleRowAsync(
            descriptor,
            row,
            StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor),
            cancellationToken);
        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
        }

        return new StandardCrudDetail { Fields = fields, Summaries = summaries };
    }

    // 摘要 provider 是跨模块依赖入口，失败时返回稳定状态，避免详情主数据被附属摘要拖垮。
    private async Task<StandardCrudSummaryBlock> BuildSummaryAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        string key,
        CancellationToken cancellationToken)
    {
        if (!_summaryProviders.TryGetValue((descriptor.ResourceKey, key), out var provider))
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        try
        {
            return await provider.BuildAsync(descriptor, row, cancellationToken);
        }
        catch (Exception)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }
    }

    private static IReadOnlyDictionary<string, object?> FilterFields(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<string> allowedFields)
    {
        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in allowedFields)
        {
            if (values.TryGetValue(field, out var value))
            {
                filtered[field] = value;
            }
        }

        return filtered;
    }

    // 创建默认值只补齐调用方未提交的字段；服务端拥有字段仍在更早的请求边界拒绝伪造输入。
    private static void ApplyCreateDefaults(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies)
        {
            if (!values.ContainsKey(policy.Field) && policy.CreateDefaultValue is not null)
            {
                values[policy.Field] = policy.CreateDefaultValue;
            }
        }
    }

    // 短文本存储事实在公共写入边界失败，避免各模块等待数据库截断异常或复制长度协议。
    private static StandardCrudError? ValidateFieldPolicies(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies)
        {
            if (!values.TryGetValue(policy.Field, out var value) || value is not string text)
            {
                continue;
            }

            if (policy.Trim && !string.Equals(text, text.Trim(), StringComparison.Ordinal)
                || policy.EmptyAsNull && text.Length == 0)
            {
                return FieldPolicyError(
                    policy.Field,
                    "StandardCrud.ShortTextNotNormalized",
                    $"字段 {policy.Field} 必须使用去除首尾空白后的规范短文本，空值请提交 null。",
                    "standardCrud.shortTextNotNormalized");
            }

            if (policy.MaxLength is { } maxLength && text.Length > maxLength)
            {
                return FieldPolicyError(
                    policy.Field,
                    "StandardCrud.ShortTextTooLong",
                    $"字段 {policy.Field} 不能超过 {maxLength} 个字符。",
                    "standardCrud.shortTextTooLong");
            }
        }

        return null;
    }

    // 字段策略错误统一带稳定顶层码与单字段错误，供标准表单精确定位。
    private static StandardCrudError FieldPolicyError(
        string field,
        string code,
        string message,
        string reasonKey) => new()
        {
            StatusCode = 400,
            Code = code,
            Message = message,
            ReasonKey = reasonKey,
            FieldErrors = [new StandardCrudFieldError(field, code, message)]
        };

    // 规范字段策略是值所有权的唯一来源；阶段型 owner 在过滤未知字段前拒绝越权输入。
    private static StandardCrudError? FindReadOnlyOwnershipFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        bool isCreate)
    {
        var submitted = descriptor.FieldPolicies.FirstOrDefault(policy =>
            (policy.ValueOwner == StandardCrudFieldValueOwner.Server
                || isCreate && policy.ValueOwner == StandardCrudFieldValueOwner.ServerOnCreate
                || !isCreate && policy.ValueOwner == StandardCrudFieldValueOwner.CallerOnCreate)
            && values.ContainsKey(policy.Field));
        return submitted is null
            ? null
            : submitted.ValueOwner == StandardCrudFieldValueOwner.CallerOnCreate
                ? CreateImmutableFieldError(submitted.Field)
                : CreateReadOnlyServerOwnedFieldError(submitted.Field);
    }

    // 服务端拥有字段使用字段级 400，调用方可直接移除伪造值且不会把平台事实误判为普通未知字段。
    private static StandardCrudError CreateReadOnlyServerOwnedFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.ServerOwnedFieldReadOnly",
        ReasonKey = "standardCrud.serverOwnedFieldReadOnly",
        Message = $"标准 CRUD 服务端字段 {field} 不能由客户端提交。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.ServerOwnedFieldReadOnly",
                $"标准 CRUD 服务端字段 {field} 不能由客户端提交。")
        ]
    };

    // 创建后不可变字段使用独立稳定码，避免把调用方创建事实误报成服务端生成事实。
    private static StandardCrudError CreateImmutableFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.ImmutableFieldReadOnly",
        ReasonKey = "standardCrud.immutableFieldReadOnly",
        Message = $"标准 CRUD 字段 {field} 只能在创建时提交。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.ImmutableFieldReadOnly",
                $"标准 CRUD 字段 {field} 只能在创建时提交。")
        ]
    };

    // 服务端必填审计字段不能降级为客户端输入；owner 未装配或返回无效值时在仓储调用前独立失败。
    private StandardCrudError? TryResolveRequiredAuditStamp(
        StandardCrudDescriptor descriptor,
        out EntityAuditStamp? stamp)
    {
        var requiredAuditField = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired
            && policy.ValueOwner == StandardCrudFieldValueOwner.Server
            && IsEntityAuditField(policy.Field));
        if (requiredAuditField is null)
        {
            stamp = null;
            return null;
        }

        try
        {
            stamp = _auditStampProvider?.GetCurrent();
        }
        catch (ArgumentException)
        {
            stamp = null;
        }

        return stamp is null
            ? ServerOwnedValueUnavailable(descriptor, requiredAuditField.Field)
            : null;
    }

    // ADR-0026 固定 Entity 审计四字段；主键等其他服务端字段继续由其既有 owner 处理。
    private static bool IsEntityAuditField(string field) =>
        field is "CreatedBy" or "CreatedAt" or "UpdatedBy" or "UpdatedAt";

    // owner 缺失属于平台 500，不生成字段级调用方错误，避免把平台配置缺陷归因于请求。
    private static StandardCrudError ServerOwnedValueUnavailable(
        StandardCrudDescriptor descriptor,
        string field) => new()
        {
            StatusCode = 500,
            Code = "StandardCrud.ServerOwnedValueUnavailable",
            ReasonKey = "standardCrud.serverOwnedValueUnavailable",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的服务端必填字段 {field} 无法取得可信值。"
        };

    // 只有 canonical raw/display companion 属于 output-only；legacy 别名与未知字段继续沿既有边界处理。
    private static StandardCrudError? FindReadOnlyDisplayFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var displayField in StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor))
        {
            if (!IsCanonicalDisplayCompanion(displayField)
                || !values.ContainsKey(displayField.Field))
            {
                continue;
            }

            return CreateReadOnlyDisplayFieldError(displayField.Field);
        }

        return null;
    }

    // canonical companion 以 source + "_DisplayValue" 为唯一稳定命名；其余 display 声明不参与本次写拒绝。
    private static bool IsCanonicalDisplayCompanion(ICP.Platform.Query.Descriptors.PlatformDisplayValueField displayField) =>
        string.Equals(displayField.Field, string.Concat(displayField.SourceField, "_DisplayValue"), StringComparison.Ordinal);

    // display companion 只能由服务端在读取和写后详情阶段补齐，客户端提交视为稳定 400。
    private static StandardCrudError CreateReadOnlyDisplayFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.DisplayValueFieldReadOnly",
        ReasonKey = "standardCrud.displayValueFieldReadOnly",
        Message = $"标准 CRUD 显示字段 {field} 只允许输出，不能提交写入。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.DisplayValueFieldReadOnly",
                $"标准 CRUD 显示字段 {field} 只允许输出，不能提交写入。")
        ]
    };

    // temporal 语义由字段策略触发；规范化完成后 validator 与仓储只接收 DateOnly 或 UTC DateTime。
    private StandardCrudError? NormalizeTemporalInputs(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies.Where(static policy =>
                     policy.ValueType is FieldValueType.CalendarDate or FieldValueType.Instant))
        {
            if (!values.TryGetValue(policy.Field, out var value))
            {
                continue;
            }

            if (_temporalInputNormalizer is null)
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 声明了 temporal 输入字段，但未注册平台规范化器。");
            }

            var result = _temporalInputNormalizer.Normalize(policy.ValueType, value);
            if (!result.IsSuccess)
            {
                return new StandardCrudError
                {
                    StatusCode = 400,
                    Code = result.ErrorCode!,
                    ReasonKey = "standardCrud.temporalInputInvalid",
                    Message = result.Message!,
                    FieldErrors = [new StandardCrudFieldError(policy.Field, result.ErrorCode!, result.Message!)]
                };
            }

            values[policy.Field] = result.Value;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> responseFields)
    {
        var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in responseFields)
        {
            projected[field] = row.TryGetValue(field, out var value) ? value : null;
        }

        return projected;
    }

    // 必填校验属于标准写入前置校验；创建检查缺失，更新只检查本次提交字段是否被清空。
    private static StandardCrudError? ValidateRequiredFields(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        bool isCreate)
    {
        foreach (var requiredField in descriptor.RequiredFields)
        {
            if (!AppliesToMutation(requiredField.Scope, isCreate))
            {
                continue;
            }

            if (isCreate)
            {
                if (!values.TryGetValue(requiredField.Field, out var value) || IsMissingRequiredValue(value))
                {
                    return RequiredFieldError(requiredField);
                }

                continue;
            }

            if (values.TryGetValue(requiredField.Field, out var updateValue) && IsMissingRequiredValue(updateValue))
            {
                return RequiredFieldError(requiredField);
            }
        }

        return null;
    }

    private static bool AppliesToMutation(StandardCrudRequiredFieldScope scope, bool isCreate)
    {
        return isCreate
            ? scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate
            : scope is StandardCrudRequiredFieldScope.UpdateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;
    }

    private static bool IsMissingRequiredValue(object? value)
    {
        return value is null || value is string text && string.IsNullOrWhiteSpace(text);
    }

    private static StandardCrudError RequiredFieldError(StandardCrudRequiredField requiredField) => new()
    {
        StatusCode = 400,
        Code = requiredField.ErrorCode,
        Message = requiredField.Message,
        ReasonKey = requiredField.ReasonKey,
        FieldErrors = [new StandardCrudFieldError(requiredField.Field, requiredField.ErrorCode, requiredField.Message)]
    };

    // 合并后的完整候选必须满足由调用方拥有的存储非空不变量；服务端拥有值由其可信持久化 owner 在写入边界补齐。
    private static StandardCrudError? ValidateStorageInvariants(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> candidate)
    {
        var invalid = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired
            && policy.ValueOwner != StandardCrudFieldValueOwner.Server
            && (!candidate.TryGetValue(policy.Field, out var value) || IsMissingRequiredValue(value)));
        return invalid is null
            ? null
            : new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.StorageInvariantViolation",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的存储必填字段 {invalid.Field} 当前为空。",
                ReasonKey = "standardCrud.storageInvariantViolation"
            };
    }

    // 唯一约束属于标准写入前置校验；descriptor 只声明约束，执行顺序由内核统一控制。
    private async Task<StandardCrudError?> ValidateUniqueConstraintsAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        string? excludedId,
        CancellationToken cancellationToken)
    {
        foreach (var constraint in descriptor.UniqueConstraints)
        {
            var equalityValues = BuildUniqueEqualityValues(constraint, values);
            if (equalityValues is null)
            {
                continue;
            }

            if (await _repository.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken))
            {
                return new StandardCrudError
                {
                    StatusCode = 409,
                    Code = constraint.ErrorCode,
                    Message = constraint.Message,
                    ReasonKey = constraint.ReasonKey,
                    FieldErrors = constraint.Fields.Count == 0
                        ? []
                        : [new StandardCrudFieldError(constraint.Fields[0], constraint.ErrorCode, constraint.Message)]
                };
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?>? BuildUniqueEqualityValues(
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> values)
    {
        var equalityValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
        {
            if (!values.TryGetValue(field, out var value) || value is null)
            {
                if (constraint.IgnoreWhenAnyFieldMissing)
                {
                    return null;
                }

                equalityValues[field] = null;
                continue;
            }

            equalityValues[field] = NormalizeUniqueValue(value, constraint);
        }

        return equalityValues;
    }

    private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
    {
        if (value is not string text)
        {
            return value;
        }

        var normalized = constraint.TrimWhitespace ? text.Trim() : text;
        return constraint.CaseInsensitive ? normalized.ToUpperInvariant() : normalized;
    }

    // 规范策略存在时由可信 Entity audit owner 一次性生成四字段；未采用策略的资源保留既有 CreatedAt 行为。
    private static void ApplyCreateAuditFields(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values,
        EntityAuditStamp? stamp)
    {
        var serverOwnedAuditPolicies = descriptor.FieldPolicies
            .Where(policy =>
                policy.ValueOwner == StandardCrudFieldValueOwner.Server
                && IsEntityAuditField(policy.Field))
            .ToArray();
        if (serverOwnedAuditPolicies.Length > 0)
        {
            var trustedStamp = stamp ?? throw new InvalidOperationException("Required Entity audit stamp was not resolved.");
            foreach (var policy in serverOwnedAuditPolicies)
            {
                values[policy.Field] = policy.Field switch
                {
                    "CreatedBy" or "UpdatedBy" => trustedStamp.ActorId,
                    "CreatedAt" or "UpdatedAt" => trustedStamp.OccurredAtUtc,
                    _ => throw new InvalidOperationException($"Unsupported Entity audit field '{policy.Field}'.")
                };
            }

            return;
        }

        if (descriptor.FieldSet.DetailFields.Contains("CreatedAt", StringComparer.Ordinal))
        {
            values["CreatedAt"] = DateTime.UtcNow;
        }
    }

    // 规范策略存在时只刷新服务端拥有的更新审计字段；未采用策略的资源保留既有 UpdatedAt 行为。
    private static void ApplyUpdateAuditFields(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values,
        EntityAuditStamp? stamp)
    {
        var serverOwnedUpdateAuditPolicies = descriptor.FieldPolicies
            .Where(policy =>
                policy.ValueOwner == StandardCrudFieldValueOwner.Server
                && policy.Field is "UpdatedBy" or "UpdatedAt")
            .ToArray();
        if (serverOwnedUpdateAuditPolicies.Length > 0)
        {
            var trustedStamp = stamp ?? throw new InvalidOperationException("Required Entity audit stamp was not resolved.");
            foreach (var policy in serverOwnedUpdateAuditPolicies)
            {
                values[policy.Field] = policy.Field == "UpdatedBy"
                    ? trustedStamp.ActorId
                    : trustedStamp.OccurredAtUtc;
            }

            return;
        }

        if (descriptor.FieldSet.UpdateFields.Contains("UpdatedAt", StringComparer.Ordinal))
        {
            values["UpdatedAt"] = DateTime.UtcNow;
        }
    }

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

    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的对象 {id} 不存在。",
        ReasonKey = "standardCrud.notFound"
    };

    // 数据库约束是校验后的最终并发裁决；公共边界只暴露稳定冲突，不返回 provider 细节。
    private static StandardCrudError PersistenceConflict(StandardCrudDescriptor descriptor) => new()
    {
        StatusCode = 409,
        Code = "StandardCrud.PersistenceConflict",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的关联数据已发生变化，请重新加载后重试。",
        ReasonKey = "standardCrud.persistenceConflict"
    };
}
