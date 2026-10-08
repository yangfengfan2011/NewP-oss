using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;

namespace ICP.Platform.Query.Runtime;

/// <summary>
/// 根据查询 descriptor 和调用方请求计算可响应字段、数据库投影字段及 display value 依赖字段。
/// 所有字段边界校验都收口在这里，避免上层 adapter 依赖隐式约定。
/// </summary>
public static class PlatformQueryFieldPlanner
{
    /// <summary>
    /// 根据 descriptor 和请求生成字段规划结果。
    /// </summary>
    /// <param name="descriptor">查询资源边界描述。</param>
    /// <param name="request">调用方字段与分页意图。</param>
    /// <returns>供查询执行器和后续 display resolver 使用的字段规划。</returns>
    /// <exception cref="ArgumentNullException">descriptor 或 request 为空。</exception>
    /// <exception cref="PlatformQueryValidationException">请求字段越界或尝试按 display value 排序/筛选。</exception>
    public static PlatformQueryFieldPlan Build(PlatformQueryDescriptor descriptor, PlatformQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);

        var allowedModelFields = descriptor.AllowedModelFields.ToHashSet(StringComparer.Ordinal);
        var displayFields = ValidateDescriptorInvariants(descriptor, allowedModelFields);
        var allowedResponseFields = allowedModelFields
            .Concat(displayFields.Keys)
            .ToHashSet(StringComparer.Ordinal);
        var requestedResponseFields = NormalizeFields(request.Fields, descriptor.DefaultFields);
        if (requestedResponseFields.Count == 0)
        {
            requestedResponseFields = NormalizeFields(null, descriptor.DefaultFields);
        }

        foreach (var field in requestedResponseFields)
        {
            if (!allowedResponseFields.Contains(field))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.UnknownField",
                    $"查询资源 {descriptor.ResourceKey} 不允许请求字段 {field}。");
            }
        }

        var dbFields = new List<string>();
        AddField(dbFields, descriptor.IdField);

        foreach (var field in requestedResponseFields.Where(allowedModelFields.Contains))
        {
            AddField(dbFields, field);
        }

        var (sortField, _) = ResolveSort(descriptor, request, allowedModelFields, displayFields);
        if (!string.IsNullOrWhiteSpace(sortField))
        {
            AddField(dbFields, sortField);
        }

        if (request.Conditions is { Count: > 0 })
        {
            foreach (var condition in request.Conditions)
            {
                ValidateModelField(descriptor, allowedModelFields, displayFields, condition.Field, "condition");
                AddField(dbFields, condition.Field);
            }
        }

        var requestedDisplayFields = new List<PlatformDisplayValueField>();
        foreach (var field in requestedResponseFields)
        {
            if (displayFields.TryGetValue(field, out var displayField))
            {
                ValidateModelField(descriptor, allowedModelFields, displayFields, displayField.SourceField, "displaySource");
                AddField(dbFields, displayField.SourceField);
                requestedDisplayFields.Add(displayField);
            }
        }

        var internalOnlyFields = dbFields
            .Where(field => !requestedResponseFields.Contains(field, StringComparer.Ordinal))
            .ToArray();

        return new PlatformQueryFieldPlan(
            ResponseFields: requestedResponseFields,
            DbSelectFields: dbFields,
            InternalOnlyFields: internalOnlyFields,
            RequestedDisplayFields: requestedDisplayFields);
    }

    /// <summary>
    /// 解析并校验单列排序请求；字段必须属于 descriptor 排序白名单，方向只允许 asc/desc。
    /// </summary>
    public static (string? Field, string? Direction) ResolveSort(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request)
    {
        var allowedModelFields = descriptor.AllowedModelFields.ToHashSet(StringComparer.Ordinal);
        var displayFields = ValidateDescriptorInvariants(descriptor, allowedModelFields);
        return ResolveSort(descriptor, request, allowedModelFields, displayFields);
    }

    // 排序解析复用已建立的 descriptor 集合，避免字段规划重复创建白名单。
    private static (string? Field, string? Direction) ResolveSort(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        ISet<string> allowedModelFields,
        IReadOnlyDictionary<string, PlatformDisplayValueField> displayFields)
    {
        var sortField = string.IsNullOrWhiteSpace(request.SortBy) ? descriptor.DefaultSortField : request.SortBy.Trim();
        if (string.IsNullOrWhiteSpace(sortField))
        {
            return (null, null);
        }

        ValidateModelField(descriptor, allowedModelFields, displayFields, sortField, "sort");
        if (!descriptor.SortableFields.Contains(sortField, StringComparer.Ordinal))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.UnknownSortField",
                $"查询资源 {descriptor.ResourceKey} 不允许按字段 {sortField} 排序。");
        }

        var direction = string.IsNullOrWhiteSpace(request.SortDirection)
            ? descriptor.DefaultSortDirection
            : request.SortDirection.Trim().ToLowerInvariant();
        if (direction is not ("asc" or "desc"))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidSortDirection",
                $"查询资源 {descriptor.ResourceKey} 不支持排序方向 {direction}。");
        }

        return (sortField, direction);
    }

    /// <summary>
    /// 在字段规划前收口 descriptor 的静态约束，避免下游执行依赖隐式覆盖或字典异常。
    /// </summary>
    private static IReadOnlyDictionary<string, PlatformDisplayValueField> ValidateDescriptorInvariants(
        PlatformQueryDescriptor descriptor,
        ISet<string> allowedModelFields)
    {
        if (!allowedModelFields.Contains(descriptor.IdField))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidDescriptorIdField",
                $"查询资源 {descriptor.ResourceKey} 的 IdField {descriptor.IdField} 不在 AllowedModelFields 中。");
        }

        var displayFields = new Dictionary<string, PlatformDisplayValueField>(StringComparer.Ordinal);
        foreach (var displayField in descriptor.DisplayValueFields)
        {
            if (displayFields.ContainsKey(displayField.Field))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.DuplicateDisplayField",
                    $"查询资源 {descriptor.ResourceKey} 重复声明了 display value 字段 {displayField.Field}。");
            }

            if (allowedModelFields.Contains(displayField.Field))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.DisplayFieldConflictsWithModelField",
                    $"查询资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 与真实模型字段重名。");
            }

            if (!allowedModelFields.Contains(displayField.SourceField))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.InvalidDisplaySourceField",
                    $"查询资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 依赖了未声明的 SourceField {displayField.SourceField}。");
            }

            displayFields.Add(displayField.Field, displayField);
        }

        return displayFields;
    }

    private static List<string> NormalizeFields(IReadOnlyList<string>? fields, IReadOnlyList<string> fallback)
    {
        var source = fields is { Count: > 0 } ? fields : fallback;
        var normalized = new List<string>(source.Count);

        foreach (var field in source)
        {
            var value = field.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            AddField(normalized, value);
        }

        return normalized;
    }

    /// <summary>
    /// 校验排序/筛选字段是否仍然停留在真实模型字段边界内。
    /// display value 在第一版中只允许响应，不允许进入 SQL 排序或筛选。
    /// </summary>
    private static void ValidateModelField(
        PlatformQueryDescriptor descriptor,
        ISet<string> allowedModelFields,
        IReadOnlyDictionary<string, PlatformDisplayValueField> displayFields,
        string field,
        string purpose)
    {
        if (displayFields.ContainsKey(field) && purpose is "sort" or "condition")
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.DisplayValueSortOrFilterUnsupported",
                $"查询资源 {descriptor.ResourceKey} 第一版不支持按 display value 字段 {field} 排序或筛选。");
        }

        if (!allowedModelFields.Contains(field))
        {
            var code = purpose == "condition" ? "PlatformQuery.UnknownConditionField" : "PlatformQuery.UnknownField";
            throw new PlatformQueryValidationException(
                code,
                $"查询资源 {descriptor.ResourceKey} 不允许使用字段 {field}。");
        }
    }

    private static void AddField(ICollection<string> fields, string field)
    {
        if (!fields.Contains(field, StringComparer.Ordinal))
        {
            fields.Add(field);
        }
    }
}
