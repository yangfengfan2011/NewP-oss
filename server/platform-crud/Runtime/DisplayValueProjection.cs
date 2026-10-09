using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Crud.Runtime;

/// <summary>CRUD 与 Link 共用的显示投影执行模块；只消费资源身份、typed plan 和本次请求字段。</summary>
public sealed class DisplayValueProjection
{
    private readonly IReadOnlyDictionary<string, IStandardCrudDisplayValueResolver> _displayResolvers;
    private readonly ITemporalDisplayProjectionExecutor? _temporalDisplayProjectionExecutor;
    private readonly IUserDisplayNameDirectory? _userDisplayNameDirectory;
    private readonly IRequestPresentationContextAccessor? _presentationContextAccessor;
    private readonly IRuntimeTextCatalog? _textCatalog;
    private readonly ILogger<DisplayValueProjection>? _logger;

    /// <summary>组合批量解析与请求展示依赖，不依赖 CRUD 路由、字段写入策略或持久化注册表。</summary>
    public DisplayValueProjection(
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        ITemporalDisplayProjectionExecutor? temporalDisplayProjectionExecutor = null,
        IUserDisplayNameDirectory? userDisplayNameDirectory = null,
        IRequestPresentationContextAccessor? presentationContextAccessor = null,
        IRuntimeTextCatalog? textCatalog = null,
        ILogger<DisplayValueProjection>? logger = null)
    {
        _displayResolvers = displayResolvers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _temporalDisplayProjectionExecutor = temporalDisplayProjectionExecutor;
        _userDisplayNameDirectory = userDisplayNameDirectory;
        _presentationContextAccessor = presentationContextAccessor;
        _textCatalog = textCatalog;
        _logger = logger;
    }

    /// <summary>复制并补齐本批原始行；拒绝覆盖 raw、未请求字段、错误行数及非字符串 companion。</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApplyDisplayValuesAsync(
        string resourceKey,
        ModelDisplayProjectionPlan? displayPlan,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<ICP.Platform.Query.Descriptors.PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = rows
            .Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(row, StringComparer.Ordinal))
            .ToList();
        if (requestedDisplayFields.Count == 0)
        {
            return values;
        }

        if (rows.Any(row => requestedDisplayFields.Any(field => row.ContainsKey(field.Field))))
        {
            throw new InvalidOperationException($"DISPLAY_RAW_FIELD_CONFLICT: {resourceKey}");
        }

        var temporalPlans = displayPlan?.Fields
            .Where(plan => requestedDisplayFields.Any(field => string.Equals(field.Field, plan.CompanionField, StringComparison.Ordinal)))
            .Where(static plan => plan is TemporalFieldDisplayProjectionPlan or CalendarDateFieldDisplayProjectionPlan)
            .ToDictionary(static plan => plan.CompanionField, StringComparer.Ordinal)
            ?? new Dictionary<string, FieldDisplayProjectionPlan>(StringComparer.Ordinal);
        var auditUserPlans = displayPlan?.Fields
            .OfType<LookupFieldDisplayProjectionPlan>()
            .Where(plan => plan.IsEntityAuditField && requestedDisplayFields.Any(field => field.Field == plan.CompanionField))
            .ToArray() ?? [];
        var lookupFields = requestedDisplayFields
            .Where(field => !temporalPlans.ContainsKey(field.Field))
            .Where(field => !auditUserPlans.Any(plan => plan.CompanionField == field.Field))
            .ToArray();

        IStandardCrudDisplayValueResolver? resolver = null;
        if (lookupFields.Length > 0 && !_displayResolvers.TryGetValue(resourceKey, out resolver))
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {resourceKey} 请求了显示值字段，但未注册显示值解析器。");
        }

        if (temporalPlans.Count > 0 && _temporalDisplayProjectionExecutor is null)
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {resourceKey} 请求了 temporal 显示值字段，但未注册平台执行器。");
        }

        if (values.Count == 0)
        {
            return values;
        }

        var resolvedRows = resolver is null
            ? rows.Select(static _ => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>()).ToArray()
            : await resolver.ResolveListAsync(rows, lookupFields, cancellationToken);
        if (resolvedRows.Count != values.Count)
        {
            throw new InvalidOperationException(
                $"标准 CRUD 资源 {resourceKey} 的显示值解析器返回行数 {resolvedRows.Count}，预期 {values.Count}。");
        }

        var allowedDisplayFields = lookupFields
            .Select(static field => field.Field)
            .ToHashSet(StringComparer.Ordinal);
        var auditNames = await ResolveAuditNamesAsync(rows, auditUserPlans, cancellationToken);
        for (var index = 0; index < values.Count; index++)
        {
            foreach (var pair in resolvedRows[index])
            {
                if (rows[index].ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {resourceKey} 的显示值解析器尝试覆盖原始字段 {pair.Key}。");
                }

                if (!allowedDisplayFields.Contains(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {resourceKey} 的显示值解析器返回未请求字段 {pair.Key}。");
                }

                if (pair.Value is not null and not string)
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 资源 {resourceKey} 的显示值解析器返回字段 {pair.Key} 的值必须是 string 或 null。");
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
                        $"标准 CRUD 资源 {resourceKey} 的 temporal 字段 {plan.SourceField} 与计划类型不匹配。")
                };
                ((Dictionary<string, object?>)values[index])[companionField] = displayValue;
            }

            foreach (var plan in auditUserPlans)
            {
                var raw = ReadAuditActor(rows[index], plan.SourceField);
                var display = raw == SystemEntityAuditStampProvider.SystemActorId
                    ? _textCatalog?.GetText("Platform.SystemActor", _presentationContextAccessor?.Current?.Locale)
                    : raw is null || auditNames is null ? null
                    : auditNames.TryGetValue(raw, out var name) ? name : raw;
                ((Dictionary<string, object?>)values[index])[plan.CompanionField] = display;
            }
        }

        return values;
    }

    // 一个读取批次合并两列全部账号；只有非关键目录调用失败可以降级，结构校验留在 catch 外。
    private async Task<IReadOnlyDictionary<string, string>?> ResolveAuditNamesAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<LookupFieldDisplayProjectionPlan> plans,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = rows.SelectMany(row => plans.Select(plan => ReadAuditActor(row, plan.SourceField)))
            .Where(id => id is not null && id != SystemEntityAuditStampProvider.SystemActorId)
            .Cast<string>().ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>();
        }
        IReadOnlyDictionary<string, string> names;
        try
        {
            if (_userDisplayNameDirectory is null)
            {
                throw new InvalidOperationException("AUDIT_NAME_DIRECTORY_UNAVAILABLE");
            }
            names = await _userDisplayNameDirectory.FindManyAsync(ids, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not UserDisplayNameDirectoryContractException
            and not ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException)
        {
            _logger?.LogWarning("AUDIT_NAME_DIRECTORY_UNAVAILABLE: {FailureType}", exception.GetType().Name);
            return null;
        }
        if (names is null || names.Any(pair => !ids.Contains(pair.Key) || pair.Value is null))
        {
            throw new UserDisplayNameDirectoryContractException();
        }
        return names;
    }

    // 审计 ID 类型是公共契约；不能把结构错误当作名称未命中或字符串转换。
    private static string? ReadAuditActor(IReadOnlyDictionary<string, object?> row, string field) =>
        !row.TryGetValue(field, out var value) || value is null ? null
        : value is string id ? id
        : throw new InvalidOperationException($"AUDIT_ACTOR_TYPE_INVALID: {field}");

}
