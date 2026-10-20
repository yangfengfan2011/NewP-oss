using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Crud.ObjectOperations;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly ICP.Platform.Authorization.Contracts.IDataScopeEvaluationScope? _evaluation;
    private readonly IStandardCrudRepository _repository;
    private readonly IPlatformQueryExecutor _queryExecutor;
    private readonly IReadOnlyDictionary<string, IStandardCrudQueryCustomizer> _queryCustomizers;
    private readonly DisplayValueProjection _displayProjection;
    private readonly IReadOnlyDictionary<(string ResourceKey, string Key), IStandardCrudSummaryProvider> _summaryProviders;
    private readonly ILogger<StandardCrudService>? _logger;
    private readonly IObjectOperationExecutor? _objectOperations;
    private readonly StandardCrudDefaultWriter _defaultWriter;
    private readonly ICP.Platform.Authorization.Contracts.IDataScopeObjectDecisionProvider _objectScope;
    private readonly ICP.Platform.Authorization.Contracts.IDataExecutionAccess _executions;

    /// <summary>所有读取和标准写必须显式组合正式执行器；写后观察仍独立于写前裁决。</summary>
    public StandardCrudService(
        ICP.Platform.Authorization.Contracts.IDataExecutionAccess executions,
        ICP.Platform.Authorization.Contracts.IDataScopeEvaluationScope? evaluation,
        IStandardCrudRepository repository, IPlatformQueryExecutor queryExecutor,
        StandardCrudDefaultWriter defaultWriter, IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        DisplayValueProjection displayProjection, IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        ICP.Platform.Authorization.Contracts.IDataScopeObjectDecisionProvider objectScope,
        ILogger<StandardCrudService>? logger = null, IObjectOperationExecutor? objectOperations = null)
    {
        _executions = executions ?? throw new ArgumentNullException(nameof(executions));
        _evaluation = evaluation;
        _repository = repository;
        _queryExecutor = queryExecutor;
        _defaultWriter = defaultWriter;
        _queryCustomizers = queryCustomizers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _displayProjection = displayProjection;
        _summaryProviders = summaryProviders.ToDictionary(item => (item.ResourceKey, item.Key));
        _logger = logger;
        _objectOperations = objectOperations;
        _objectScope = objectScope ?? throw new ArgumentNullException(nameof(objectScope));
    }

    /// <summary>
    /// 列表读取先校验 descriptor，再将模块查询扩展收敛为平台查询条件，最后补齐显示值。
    /// </summary>
    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        using var operation = _evaluation?.BeginOperation();
        StandardCrudDescriptorValidator.Validate(descriptor);
        var conditions = BuildConditions(descriptor, query);
        var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);
        var platformRequest = new PlatformQueryRequest(
            AddDisplayCompanionFields(query.Fields, StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor, descriptor.FieldSet.ListFields)),
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

        var readContext = query.ReadContext;
        if (readContext is null && descriptor.DataExecutions.TryGetValue("list", out var listExecution))
            readContext = new(new(listExecution, "standardCrud.list", "read"), "query");
        if (readContext is not null)
            platformRequest = platformRequest with { ScopePredicate = (await _executions
                .GetQueryReadPlanAsync(readContext, descriptor.ResourceKey, cancellationToken)).Predicate };
        else throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_UNREGISTERED");
        var result = await _queryExecutor.QueryAsync(queryDescriptor, platformRequest, cancellationToken);
        var rows = await _displayProjection.ApplyDisplayValuesAsync(descriptor.ResourceKey, descriptor.DisplayProjectionPlan, result.Items, result.RequestedDisplayFields, cancellationToken);
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
    public Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id,
        CancellationToken cancellationToken = default) => GetDetailCoreAsync(descriptor, id, null, cancellationToken);

    /// <inheritdoc/>
    public Task<StandardCrudDetail?> GetDetailFromEntryAsync(StandardCrudDescriptor descriptor, string id,
        IReadOnlyList<ICP.Platform.Authorization.Contracts.DataExecutionBindingReference> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Count == 0) throw new ArgumentException("An entry read binding is required.", nameof(bindings));
        return GetDetailCoreAsync(descriptor, id, null, cancellationToken, bindings);
    }

    /// <inheritdoc/>
    public Task<StandardCrudDetail?> GetDetailFromSourceAsync(StandardCrudDescriptor descriptor, string id,
        ICP.Platform.Authorization.Contracts.DataExecutionInternalReadBinding source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return GetDetailCoreAsync(descriptor, id, source, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<StandardCrudDetail?> GetDetailFromApiAsync(StandardCrudDescriptor descriptor, string id,
        ICP.Platform.Authorization.Contracts.DataExecutionApiObjectContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return GetDetailCoreAsync(descriptor, id, null, cancellationToken, apiSource: source);
    }

    // 原详情SQL/摘要/display流程不变；明确的内部来源在同一原位取得一次读取计划。
    private async Task<StandardCrudDetail?> GetDetailCoreAsync(StandardCrudDescriptor descriptor, string id,
        ICP.Platform.Authorization.Contracts.DataExecutionInternalReadBinding? source, CancellationToken cancellationToken,
        IReadOnlyList<ICP.Platform.Authorization.Contracts.DataExecutionBindingReference>? entryBindings = null,
        ICP.Platform.Authorization.Contracts.DataExecutionApiObjectContext? apiSource = null)
    {
        using var operation = _evaluation?.BeginOperation();
        StandardCrudDescriptorValidator.Validate(descriptor);
        var bindings = entryBindings ?? (source is null && apiSource is null ? ICP.Platform.Crud.Extensions.StandardCrudDataExecutions.DetailReadBindings(descriptor) : []);
        if (source is not null || apiSource is not null || bindings.Count > 0)
        {
            var scope = apiSource is not null
                ? await _executions
                    .GetApiObjectReadPlanAsync(apiSource, descriptor.ResourceKey, id, cancellationToken)
                : source is not null
                ? await _executions
                    .GetInternalReadPlanAsync(source, descriptor.ResourceKey, [id], cancellationToken)
                : await _executions
                    .GetObjectReadPlanAsync(bindings, descriptor.ResourceKey, "target", id, cancellationToken);
            if (scope.Predicate is not null)
            {
                var visible = await _queryExecutor.QueryAsync(StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor),
                    new PlatformQueryRequest([descriptor.IdField], PageSize: 1, Conditions: [new(descriptor.IdField, "=", id)])
                    { ScopePredicate = scope.Predicate }, cancellationToken);
                if (visible.Total == 0) return null;
            }
        }
        else throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_UNREGISTERED");
        var row = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var fields = await ApplyDisplayValuesToSingleRowAsync(
            descriptor,
            row,
            StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor, descriptor.FieldSet.DetailFields),
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
        if (descriptor.DataExecutions.TryGetValue("options", out var execution))
            query = query with { ReadContext = new(new(execution, "standardCrud.options", "read"), "query")
                { TargetSets = [new("current", [])] } };
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
        if (descriptor.DataExecutions.TryGetValue("options", out var execution))
        {
            var ids = (request.CurrentValueIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
            request = request with { CurrentValueIds = ids, ReadContext = new(new(execution, "standardCrud.options", "read"), "query")
                { TargetSets = [new("current", ids)] } };
        }
        return _repository.LookupAsync(descriptor, request, cancellationToken);
    }

    /// <summary>
    /// 按 descriptor 校验并创建资源；canonical display companion 只允许输出，写入前过滤业务白名单字段，
    /// 并将校验后发生的数据库约束竞争投影为稳定公共冲突；写后读取资格独立决定详情或回执。
    /// </summary>
    public async Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        using var operationScope = _evaluation?.BeginOperation();
        var source = EntryWriteSource(descriptor, "create", null);
        if (_objectOperations?.CanExecute(descriptor.ResourceKey, ObjectOperationAction.Create) == true)
        {
            try
            {
                var operation = await _objectOperations.ExecuteAsync(
                    descriptor.ResourceKey,
                    ObjectOperationAction.Create,
                    null,
                    values,
                    source,
                    cancellationToken);
                return await BuildMutationDetailAsync(
                    descriptor,
                    operation,
                    "Object create behavior returned no created row.",
                    cancellationToken);
            }
            catch (StandardCrudPersistenceConflictException)
            {
                return StandardCrudMutationResult.Failure(StandardCrudDefaultWriter.PersistenceConflict(descriptor));
            }
        }

        var result = await _defaultWriter.CreateAsync(descriptor, values, source, cancellationToken);
        return await BuildMutationDetailAsync(
            descriptor,
            result,
            "Default create behavior returned no created row.",
            cancellationToken);
    }

    /// <summary>
    /// 按描述符白名单更新资源；必需业务记录的资源从保护旧值到登记都在唯一事务中完成。
    /// 既有 revision 资源保留条件更新协议，数据库约束竞争统一投影为稳定公共冲突。
    /// </summary>
    public async Task<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        using var operationScope = _evaluation?.BeginOperation();
        var source = EntryWriteSource(descriptor, "update", id);
        if (_objectOperations?.CanExecute(descriptor.ResourceKey, ObjectOperationAction.Update) == true)
        {
            try
            {
                var operation = await _objectOperations.ExecuteAsync(
                    descriptor.ResourceKey,
                    ObjectOperationAction.Update,
                    id,
                    values,
                    source,
                    cancellationToken);
                return await BuildMutationDetailAsync(
                    descriptor,
                    operation,
                    "Object update behavior returned no updated row.",
                    cancellationToken);
            }
            catch (StandardCrudPersistenceConflictException)
            {
                return StandardCrudMutationResult.Failure(StandardCrudDefaultWriter.PersistenceConflict(descriptor));
            }
        }

        var result = await _defaultWriter.UpdateAsync(descriptor, id, values, source, cancellationToken);
        return await BuildMutationDetailAsync(
            descriptor,
            result,
            "Default update behavior returned no updated row.",
            cancellationToken);
    }

    /// <summary>停用仅生成固定状态写入，复用标准更新的校验和事务。</summary>
    public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(descriptor, id, false, cancellationToken);

    /// <summary>启用仅生成固定状态写入，不接收调用方的其他更新字段。</summary>
    public Task<StandardCrudMutationResult> EnableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(descriptor, id, true, cancellationToken);

    // 两个正式行为共用字段与领域校验，方向由执行端固定，不能从请求 body 替换。
    private async Task<StandardCrudMutationResult> SetEnabledAsync(StandardCrudDescriptor descriptor, string id, bool enabled, CancellationToken cancellationToken)
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

        return await UpdateAsync(descriptor, id, new Dictionary<string, object?> { [descriptor.EnabledField] = enabled }, cancellationToken);
    }

    /// <summary>按资源声明提前协调条件删除与普通删除，并统一发布提交结果。</summary>
    public async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        using var operationScope = _evaluation?.BeginOperation();
        var source = EntryWriteSource(descriptor, "delete", id);
        if (_objectOperations?.CanExecute(descriptor.ResourceKey, ObjectOperationAction.Delete) == true)
        {
            return await _objectOperations.ExecuteAsync(
                descriptor.ResourceKey,
                ObjectOperationAction.Delete,
                id,
                new Dictionary<string, object?>(StringComparer.Ordinal),
                source,
                cancellationToken);
        }

        return await _defaultWriter.DeleteAsync(descriptor, id, source, cancellationToken);
    }

    // 仅已登记的入口生成本次目标绑定；正式来源仍由统一执行器核对原请求声明。
    private static ObjectOperationWriteSource EntryWriteSource(StandardCrudDescriptor descriptor, string operation, string? id)
        => descriptor.DataExecutions.TryGetValue(operation, out var execution)
            ? new ObjectOperationWriteSource.Declared(new(new(execution, "standardCrud." + operation, "submit"), descriptor.ResourceKey, id))
            : throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_UNREGISTERED");

    // 查询扩展点只产出标准条件；列表真正执行统一收口到平台查询内核，避免模块逻辑直接进入持久化实现。
    private IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        return _queryCustomizers.TryGetValue(descriptor.ResourceKey, out var customizer)
            ? customizer.BuildConditions(descriptor, query)
            : [];
    }

    // 单行读取与列表经过同一批量显示投影入口。
    private async Task<IReadOnlyDictionary<string, object?>> ApplyDisplayValuesToSingleRowAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken)
    {
        var resolvedRows = await _displayProjection.ApplyDisplayValuesAsync(descriptor.ResourceKey, descriptor.DisplayProjectionPlan, [row], requestedDisplayFields, cancellationToken);
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
            StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor, descriptor.FieldSet.DetailFields),
            cancellationToken);
        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
        }

        return new StandardCrudDetail { Fields = fields, Summaries = summaries };
    }

    // 对象行为只返回实际持久化字段；提交后有读取资格才补齐 display 与 summary，否则只交成功回执。
    private async Task<StandardCrudMutationResult> BuildMutationDetailAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudMutationResult result,
        string missingRowMessage,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            return result;
        }

        var row = result.Detail?.Fields
            ?? throw new InvalidOperationException(missingRowMessage);
        using var observation = _evaluation?.BeginReadAfterWrite();
        var id = Convert.ToString(row.GetValueOrDefault(descriptor.IdField))
            ?? throw new InvalidOperationException("STANDARD_CRUD_MUTATION_ID_MISSING");
        try
        {
            if (!await _objectScope.IsAllowedAsync(descriptor.ResourceKey, id, "read", cancellationToken))
                return StandardCrudMutationResult.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 提交后的读取只是呈现；故障不能倒转已提交的业务成功。
            _logger?.LogWarning("STANDARD_CRUD_POST_WRITE_READ_UNAVAILABLE: {FailureType}", exception.GetType().Name);
            return StandardCrudMutationResult.Success();
        }
        try
        {
            return StandardCrudMutationResult.Success(
                await BuildDetailFromRowAsync(descriptor, row, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning("STANDARD_CRUD_POST_WRITE_DETAIL_UNAVAILABLE: {FailureType}", exception.GetType().Name);
            return StandardCrudMutationResult.Success();
        }
    }

    // 普通附属摘要可降级；关键缓存不可用及请求取消必须上抛，不能伪装成业务数据缺失。
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
        catch (Exception exception) when (exception is not ICP.Platform.Persistence.Transactions.PlatformCacheUnavailableException
            and not ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException
            and not OperationCanceledException)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }
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

}
