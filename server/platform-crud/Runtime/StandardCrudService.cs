using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;

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

    public StandardCrudService(
        IStandardCrudRepository repository,
        IPlatformQueryExecutor queryExecutor,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudActionHandler> actionHandlers,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards,
        IEnumerable<IStandardCrudConditionalDeleteExecutor>? conditionalDeleteExecutors = null)
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
            AddDisplayCompanionFields(query.Fields, descriptor.DisplayValueFields),
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

        var fields = await ApplyDisplayValuesToSingleRowAsync(descriptor, row, descriptor.DisplayValueFields, cancellationToken);
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
    /// 按 descriptor 校验并创建资源；canonical display companion 只允许输出，写入前过滤业务白名单字段并返回带详情的创建结果。
    /// </summary>
    public async Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var readOnlyDisplayFieldError = FindReadOnlyDisplayFieldError(descriptor, values);
        if (readOnlyDisplayFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(readOnlyDisplayFieldError);
        }

        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.CreateFields),
            StringComparer.Ordinal);
        ApplyCreateAuditFields(descriptor, allowed);
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: true);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
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

        var created = await _repository.CreateAsync(descriptor, allowed, cancellationToken);
        return StandardCrudMutationResult.Success(await BuildDetailFromRowAsync(descriptor, created, cancellationToken));
    }

    /// <summary>
    /// 按描述符白名单更新资源；声明 revision 字段的资源必须提交详情版本并通过仓储条件更新。
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

        var readOnlyDisplayFieldError = FindReadOnlyDisplayFieldError(descriptor, values);
        if (readOnlyDisplayFieldError is not null)
        {
            return StandardCrudMutationResult.Failure(readOnlyDisplayFieldError);
        }

        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.UpdateFields),
            StringComparer.Ordinal);
        ApplyUpdateAuditFields(descriptor, allowed);
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: false);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
        }

        var candidate = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
        foreach (var pair in allowed)
        {
            candidate[pair.Key] = pair.Value;
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

        var updated = expectedRevision.HasValue
            ? await _repository.UpdateAsync(descriptor, id, allowed, expectedRevision.Value, cancellationToken)
            : await _repository.UpdateAsync(descriptor, id, allowed, cancellationToken);
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

    // 显示值补齐以 row copy 方式合并，并拒绝 resolver 覆盖查询返回的原始字段集合。
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

        if (!_displayResolvers.TryGetValue(descriptor.ResourceKey, out var resolver))
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {descriptor.ResourceKey} 请求了显示值字段，但未注册显示值解析器。");
        }

        EnsureDisplayResolverImplementation(descriptor, resolver);
        if (values.Count == 0)
        {
            return values;
        }

        var resolvedRows = await resolver.ResolveListAsync(
            descriptor,
            rows,
            requestedDisplayFields,
            cancellationToken);
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

                ((Dictionary<string, object?>)values[index])[pair.Key] = pair.Value;
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
        var fields = await ApplyDisplayValuesToSingleRowAsync(descriptor, row, descriptor.DisplayValueFields, cancellationToken);
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

    // 只有 canonical raw/display companion 属于 output-only；legacy 别名与未知字段继续沿既有边界处理。
    private static StandardCrudError? FindReadOnlyDisplayFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var displayField in descriptor.DisplayValueFields)
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
        ReasonKey = requiredField.ReasonKey
    };

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
                    ReasonKey = constraint.ReasonKey
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

    // CreatedAt 属于创建行为的内核责任；资源只要在详情中暴露该审计字段，就由内核生成而不是要求客户端提交。
    private static void ApplyCreateAuditFields(StandardCrudDescriptor descriptor, IDictionary<string, object?> values)
    {
        if (descriptor.FieldSet.DetailFields.Contains("CreatedAt", StringComparer.Ordinal))
        {
            values["CreatedAt"] = DateTime.UtcNow;
        }
    }

    // UpdatedAt 属于更新行为的内核责任：只要资源显式把该字段纳入更新白名单，就由内核覆盖为当前时间。
    private static void ApplyUpdateAuditFields(StandardCrudDescriptor descriptor, IDictionary<string, object?> values)
    {
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
}
