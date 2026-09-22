using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ICP.Platform.Query.Runtime;

/// <summary>
/// 基于 EF shared-type entity 的平台查询执行器。
/// 它负责把字段规划结果下推到数据库查询层，避免列表查询退化为全量拉取后的内存裁剪。
/// </summary>
public sealed class EfPlatformQueryExecutor : IPlatformQueryExecutor
{
    private static readonly MethodInfo EfPropertyMethod = typeof(EF)
        .GetMethods()
        .Single(method => method.Name == nameof(EF.Property) && method.GetParameters().Length == 2);
    private static readonly MethodInfo QueryableOrderByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method => method.Name == nameof(Queryable.OrderBy) && method.GetParameters().Length == 2);
    private static readonly MethodInfo QueryableOrderByDescendingMethod = typeof(Queryable)
        .GetMethods()
        .Single(method => method.Name == nameof(Queryable.OrderByDescending) && method.GetParameters().Length == 2);
    private static readonly MethodInfo QueryableThenByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method => method.Name == nameof(Queryable.ThenBy) && method.GetParameters().Length == 2);
    private static readonly MethodInfo StringContainsMethod = typeof(string)
        .GetMethod(nameof(string.Contains), [typeof(string)])!;

    private readonly EfPlatformDbContext _dbContext;

    public EfPlatformQueryExecutor(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 执行一次平台查询；字段规划、条件翻译、排序、分页和数据库投影都在这里收口。
    /// </summary>
    public async Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;
        var plan = PlatformQueryFieldPlanner.Build(descriptor, request);
        var sort = PlatformQueryFieldPlanner.ResolveSort(descriptor, request);
        var entityType = ResolveEntityType(descriptor);

        IQueryable<Dictionary<string, object?>> query = _dbContext
            .Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
            .AsNoTracking();

        query = ApplyConditions(query, descriptor, entityType, request.Conditions ?? []);
        if (request.ScopePredicate is not null) query = query.Where(request.ScopePredicate);
        var total = await query.CountAsync(cancellationToken);

        query = ApplySort(query, descriptor, entityType, sort.Field, sort.Direction);
        query = query.Skip((page - 1) * pageSize).Take(pageSize);

        var selector = BuildSelector(plan.DbSelectFields, descriptor, entityType);
        var selectedRows = await query.Select(selector).ToListAsync(cancellationToken);
        var projectedRows = selectedRows
            .Select(values => BuildRow(plan.DbSelectFields, values))
            .ToList();

        return new PlatformQueryResult(
            Items: projectedRows,
            Total: total,
            Page: page,
            PageSize: pageSize,
            SortBy: sort.Field,
            SortDirection: sort.Direction,
            ResponseFields: plan.ResponseFields,
            RequestedDisplayFields: plan.RequestedDisplayFields);
    }

    /// <summary>
    /// 按当前条件语义构造数据库谓词。
    /// 非分组条件始终按 AND 组合；分组条件先在组内按 and/or 组合，再与其他条件按 AND 组合。
    /// </summary>
    private static IQueryable<Dictionary<string, object?>> ApplyConditions(
        IQueryable<Dictionary<string, object?>> source,
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        IReadOnlyList<PlatformQueryCondition> conditions)
    {
        var unsupported = conditions.FirstOrDefault(static condition =>
            !string.Equals(condition.Operator, "=", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(condition.Operator, "contains", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(condition.Operator, "gte", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(condition.Operator, "lte", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(condition.Operator, "isNull", StringComparison.OrdinalIgnoreCase));
        if (unsupported is not null)
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.UnsupportedConditionOperator",
                $"查询资源 {descriptor.ResourceKey} 不支持条件操作符 {unsupported.Operator}。");
        }

        var executableConditions = conditions
            .Where(static condition =>
                string.Equals(condition.Operator, "isNull", StringComparison.OrdinalIgnoreCase) ||
                condition.Value is not null &&
                (string.Equals(condition.Operator, "=", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(condition.Operator, "contains", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(condition.Operator, "gte", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(condition.Operator, "lte", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (executableConditions.Count == 0)
        {
            return source;
        }

        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        Expression? predicate = null;

        foreach (var condition in executableConditions.Where(static condition => string.IsNullOrWhiteSpace(condition.GroupKey)))
        {
            predicate = Combine(predicate, BuildConditionExpression(descriptor, entityType, row, condition), useOr: false);
        }

        foreach (var group in executableConditions
            .Where(static condition => !string.IsNullOrWhiteSpace(condition.GroupKey))
            .GroupBy(static condition => condition.GroupKey!, StringComparer.Ordinal))
        {
            var operators = group
                .Select(static condition => NormalizeGroupOperator(condition.GroupOperator))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (operators.Count > 1)
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.MixedConditionGroupOperator",
                    $"查询资源 {descriptor.ResourceKey} 的条件组 {group.Key} 不能混用组合操作符。");
            }

            var groupOperator = operators[0];
            if (!string.Equals(groupOperator, "or", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(groupOperator, "and", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.UnsupportedConditionGroupOperator",
                    $"查询资源 {descriptor.ResourceKey} 的条件组 {group.Key} 不支持组合操作符 {groupOperator}。");
            }

            Expression? groupExpression = null;
            foreach (var condition in group)
            {
                groupExpression = Combine(
                    groupExpression,
                    BuildConditionExpression(descriptor, entityType, row, condition),
                    useOr: string.Equals(groupOperator, "or", StringComparison.OrdinalIgnoreCase));
            }

            predicate = Combine(predicate, groupExpression, useOr: false);
        }

        if (predicate is null)
        {
            return source;
        }

        var lambda = Expression.Lambda<Func<Dictionary<string, object?>, bool>>(predicate, row);
        return source.Where(lambda);
    }

    // 条件操作符在查询内核统一翻译；比较条件继续使用字段的 EF CLR 类型，避免字符串比较或模块特判。
    private static Expression BuildConditionExpression(
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        ParameterExpression row,
        PlatformQueryCondition condition)
    {
        if (string.Equals(condition.Operator, "isNull", StringComparison.OrdinalIgnoreCase))
        {
            var fieldType = ResolveFieldType(descriptor, entityType, condition.Field);
            return fieldType.IsValueType && Nullable.GetUnderlyingType(fieldType) is null
                ? Expression.Constant(false)
                : Expression.Equal(BuildPropertyExpression(row, condition.Field, fieldType), Expression.Constant(null, fieldType));
        }
        if (string.Equals(condition.Operator, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return BuildContainsExpression(descriptor, entityType, row, condition);
        }
        if (string.Equals(condition.Operator, "gte", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(condition.Operator, "lte", StringComparison.OrdinalIgnoreCase))
        {
            return BuildComparisonExpression(descriptor, entityType, row, condition);
        }

        return BuildEqualsExpression(descriptor, entityType, row, condition);
    }

    private static Expression BuildContainsExpression(
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        ParameterExpression row,
        PlatformQueryCondition condition)
    {
        var fieldType = ResolveFieldType(descriptor, entityType, condition.Field);
        if (fieldType != typeof(string))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 不支持 contains 筛选。");
        }

        var property = BuildPropertyExpression(row, condition.Field, typeof(string));
        var value = ReadString(condition.Value);
        if (value.Length == 0)
        {
            return Expression.Constant(true);
        }

        var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var contains = Expression.Call(property, StringContainsMethod, Expression.Constant(value));
        return Expression.AndAlso(notNull, contains);
    }

    private static Expression BuildEqualsExpression(
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        ParameterExpression row,
        PlatformQueryCondition condition)
    {
        var fieldType = ResolveFieldType(descriptor, entityType, condition.Field);
        var property = BuildPropertyExpression(row, condition.Field, fieldType);
        var normalizedValue = NormalizeConditionValue(descriptor, condition, fieldType);
        return Expression.Equal(property, BuildConditionValueExpression(normalizedValue, fieldType));
    }

    // 范围比较先按字段类型规范化边界，再生成可由 provider 下推的闭区间谓词。
    private static Expression BuildComparisonExpression(
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        ParameterExpression row,
        PlatformQueryCondition condition)
    {
        var fieldType = ResolveFieldType(descriptor, entityType, condition.Field);
        var valueType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        if (valueType != typeof(DateOnly))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.IncompatibleConditionOperator",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 不支持操作符 {condition.Operator}。");
        }

        var property = BuildPropertyExpression(row, condition.Field, fieldType);
        var normalizedValue = NormalizeConditionValue(descriptor, condition, fieldType);
        var boundary = BuildConditionValueExpression(normalizedValue, fieldType);
        return string.Equals(condition.Operator, "gte", StringComparison.OrdinalIgnoreCase)
            ? Expression.GreaterThanOrEqual(property, boundary)
            : Expression.LessThanOrEqual(property, boundary);
    }

    // 可空数据库字段使用提升后的常量表达式，确保空值自然不匹配范围且不提前物化。
    private static Expression BuildConditionValueExpression(object value, Type fieldType)
    {
        var valueType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        var constant = Expression.Constant(value, valueType);
        return valueType == fieldType ? constant : Expression.Convert(constant, fieldType);
    }

    private static object NormalizeConditionValue(
        PlatformQueryDescriptor descriptor,
        PlatformQueryCondition condition,
        Type fieldType)
    {
        var valueType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        if (valueType == typeof(string))
        {
            return ReadString(condition.Value);
        }

        if (valueType == typeof(bool))
        {
            if (condition.Value is bool boolean)
            {
                return boolean;
            }

            if (bool.TryParse(ReadString(condition.Value), out var parsed))
            {
                return parsed;
            }

            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 需要布尔筛选值。");
        }

        if (valueType == typeof(DateTime))
        {
            if (condition.Value is DateTime dateTime)
            {
                return dateTime;
            }

            if (DateTime.TryParse(ReadString(condition.Value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 需要日期时间筛选值。");
        }

        if (valueType == typeof(DateOnly))
        {
            if (condition.Value is DateOnly dateOnly)
            {
                return dateOnly;
            }

            if (DateOnly.TryParseExact(
                ReadString(condition.Value),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            {
                return parsed;
            }

            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 需要 YYYY-MM-DD 日历日期筛选值。");
        }

        return Convert.ChangeType(condition.Value!, valueType, CultureInfo.InvariantCulture)
            ?? throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"查询资源 {descriptor.ResourceKey} 的字段 {condition.Field} 无法转换为 {valueType.Name}。");
    }

    // 主排序后依次追加 descriptor 声明的业务稳定字段与 EID，保证重复值跨页时仍有确定次序。
    private static IQueryable<Dictionary<string, object?>> ApplySort(
        IQueryable<Dictionary<string, object?>> source,
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        string? sortField,
        string? sortDirection)
    {
        if (string.IsNullOrWhiteSpace(sortField))
        {
            return source;
        }

        var fieldType = ResolveFieldType(descriptor, entityType, sortField);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var property = BuildPropertyExpression(row, sortField, fieldType);
        var keySelector = Expression.Lambda(property, row);
        var method = string.Equals(sortDirection, "desc", StringComparison.Ordinal)
            ? QueryableOrderByDescendingMethod
            : QueryableOrderByMethod;
        var genericMethod = method.MakeGenericMethod(typeof(Dictionary<string, object?>), fieldType);
        var ordered = (IOrderedQueryable<Dictionary<string, object?>>)genericMethod.Invoke(null, [source, keySelector])!;
        var appendedFields = descriptor.StableSortFields
            .Append(descriptor.IdField)
            .Where(field => !string.Equals(field, sortField, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
        foreach (var field in appendedFields)
        {
            ordered = AppendAscendingSort(ordered, descriptor, entityType, row, field);
        }

        return ordered;
    }

    // 稳定字段统一使用升序 ThenBy，调用方无法通过请求改变 descriptor 的分页裁决顺序。
    private static IOrderedQueryable<Dictionary<string, object?>> AppendAscendingSort(
        IOrderedQueryable<Dictionary<string, object?>> source,
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        ParameterExpression row,
        string field)
    {
        var fieldType = ResolveFieldType(descriptor, entityType, field);
        var selector = Expression.Lambda(BuildPropertyExpression(row, field, fieldType), row);
        var thenBy = QueryableThenByMethod.MakeGenericMethod(typeof(Dictionary<string, object?>), fieldType);
        return (IOrderedQueryable<Dictionary<string, object?>>)thenBy.Invoke(null, [source, selector])!;
    }

    private static Expression<Func<Dictionary<string, object?>, object?[]>> BuildSelector(
        IReadOnlyList<string> fields,
        PlatformQueryDescriptor descriptor,
        IEntityType entityType)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var values = fields.Select(field =>
            Expression.Convert(
                BuildPropertyExpression(row, field, ResolveFieldType(descriptor, entityType, field)),
                typeof(object)));

        return Expression.Lambda<Func<Dictionary<string, object?>, object?[]>>(
            Expression.NewArrayInit(typeof(object), values),
            row);
    }

    private static IReadOnlyDictionary<string, object?> BuildRow(IReadOnlyList<string> fields, object?[] values)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            row[fields[index]] = values[index];
        }

        return row;
    }

    private IEntityType ResolveEntityType(PlatformQueryDescriptor descriptor)
    {
        return _dbContext.Model.FindEntityType(_dbContext.ResolveModelKey(descriptor.ModelName))
            ?? _dbContext.Model.GetEntityTypes()
                .FirstOrDefault(entity =>
                    entity.ClrType == typeof(Dictionary<string, object?>) &&
                    string.Equals(entity.Name, descriptor.ModelName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"查询资源 {descriptor.ResourceKey} 的模型 {descriptor.ModelName} 未注册到 EF 平台上下文。");
    }

    private static Type ResolveFieldType(
        PlatformQueryDescriptor descriptor,
        IEntityType entityType,
        string field)
    {
        return entityType.FindProperty(field)?.ClrType
            ?? throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidModelField",
                $"查询资源 {descriptor.ResourceKey} 的字段 {field} 未注册到 EF 模型。");
    }

    private static MethodCallExpression BuildPropertyExpression(ParameterExpression row, string field, Type fieldType)
    {
        return Expression.Call(
            EfPropertyMethod.MakeGenericMethod(fieldType),
            row,
            Expression.Constant(field));
    }

    private static string NormalizeGroupOperator(string? groupOperator) =>
        string.IsNullOrWhiteSpace(groupOperator) ? "and" : groupOperator;

    private static Expression Combine(Expression? left, Expression? right, bool useOr)
    {
        if (left is null)
        {
            return right ?? Expression.Constant(true);
        }

        if (right is null)
        {
            return left;
        }

        return useOr ? Expression.OrElse(left, right) : Expression.AndAlso(left, right);
    }

    private static string ReadString(object? value) => value?.ToString() ?? string.Empty;
}
