using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudQueryCustomizer> _queryCustomizers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDisplayValueResolver> _displayResolvers;
    private readonly IReadOnlyDictionary<(string ResourceKey, string Key), IStandardCrudSummaryProvider> _summaryProviders;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly IReadOnlyDictionary<(string ResourceKey, string ActionKey), IStandardCrudActionHandler> _actionHandlers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDeleteGuard> _deleteGuards;

    public StandardCrudService(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudActionHandler> actionHandlers,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards)
    {
        _repository = repository;
        _queryCustomizers = queryCustomizers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _displayResolvers = displayResolvers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _summaryProviders = summaryProviders.ToDictionary(item => (item.ResourceKey, item.Key));
        _validators = validators.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _actionHandlers = actionHandlers.ToDictionary(item => (item.ResourceKey, item.ActionKey));
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
    }

    /// <summary>
    /// 列表读取先校验 descriptor，再将模块查询扩展收敛为仓储条件，最后补齐显示值。
    /// </summary>
    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var conditions = BuildConditions(descriptor, query);
        var result = await _repository.ListAsync(descriptor, query, conditions, cancellationToken);
        var items = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in result.Items)
        {
            items.Add(await ApplyDisplayValuesAsync(descriptor, row, cancellationToken));
        }

        return result with { Items = items };
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

        var fields = await ApplyDisplayValuesAsync(descriptor, row, cancellationToken);
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

    public async Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.CreateFields),
            StringComparer.Ordinal);
        ApplyCreateAuditFields(descriptor, allowed);
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

        var allowed = new Dictionary<string, object?>(
            FilterFields(values, descriptor.FieldSet.UpdateFields),
            StringComparer.Ordinal);
        ApplyUpdateAuditFields(descriptor, allowed);

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

        var updated = await _repository.UpdateAsync(descriptor, id, allowed, cancellationToken);
        return updated is null
            ? StandardCrudMutationResult.Failure(NotFound(descriptor, id))
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

    // 查询扩展点只产出标准条件，仓储仍负责具体执行，避免模块逻辑直接进入持久化实现。
    private IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        return _queryCustomizers.TryGetValue(descriptor.ResourceKey, out var customizer)
            ? customizer.BuildConditions(descriptor, query)
            : [];
    }

    // 显示值补齐以 row copy 方式合并，确保扩展点不能修改仓储返回的原始字段集合。
    private async Task<IReadOnlyDictionary<string, object?>> ApplyDisplayValuesAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(row, StringComparer.Ordinal);
        if (_displayResolvers.TryGetValue(descriptor.ResourceKey, out var resolver))
        {
            foreach (var pair in await resolver.ResolveAsync(descriptor, row, cancellationToken))
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    // 写入后统一按 detail 流程补齐显示值和摘要块，保证 create/update/disable 的返回结构与详情读取一致。
    private async Task<StandardCrudDetail> BuildDetailFromRowAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken)
    {
        var fields = await ApplyDisplayValuesAsync(descriptor, row, cancellationToken);
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

    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的对象 {id} 不存在。",
        ReasonKey = "standardCrud.notFound"
    };
}
