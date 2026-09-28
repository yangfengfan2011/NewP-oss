using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modeling;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>条件树的保存校验、完整判断及读取投影只有一个语义 owner。</summary>
public sealed class DataScopeConditions(DataScopeCatalog catalog, EfPlatformDbContext db, DataScopeIdentities identities,
    ITemporalInputNormalizer? temporal, IEnumerable<IDataScopeBusinessConditionProvider> providers,
    IEnumerable<IDataScopeEnumFieldProvider> enumProviders)
{
    private readonly IDataScopeBusinessConditionProvider[] providers = providers.ToArray();
    private readonly DataScopeEnumField[] enumFields = enumProviders.SelectMany(provider => provider.Fields).ToArray();
    private static readonly MethodInfo ContainsMethod = typeof(Enumerable).GetMethods().Single(method => method.Name == nameof(Enumerable.Contains)
        && method.GetParameters().Length == 2);

    public IReadOnlyList<DataScopeBusinessConditionDefinition> BusinessTypes => providers.SelectMany(provider => provider.Definitions).ToArray();

    /// <summary>向编辑器提供所属模块的枚举候选；未登记的枚举字段不能保存。</summary>
    public IReadOnlyList<string>? EnumValues(string resourceKey, string field) => enumFields.SingleOrDefault(item =>
        item.ModelKey == catalog.Resolve(resourceKey).ModelKey && item.Field == field)?.Values;

    /// <summary>登记键必须唯一，读取贡献声明和适用目标在保存前核实。</summary>
    public void ValidateRegistration()
    {
        if (BusinessTypes.Any(type => string.IsNullOrWhiteSpace(type.Key) || type.ModelKeys.Count == 0 ||
            type.ModelKeys.Any(model => string.IsNullOrWhiteSpace(model) || !db.PersistencePlan.Models.Any(item => item.LogicalKey == model))) ||
            BusinessTypes.Select(type => type.Key).Distinct(StringComparer.Ordinal).Count() != BusinessTypes.Count ||
            enumFields.Any(field => !db.PersistencePlan.Models.Any(model => model.LogicalKey == field.ModelKey &&
                    model.Fields.Any(item => item.Name == field.Field && item.ValueType == FieldValueType.Enum))
                || field.Values.Count == 0 || field.Values.Any(string.IsNullOrWhiteSpace)
                || field.Values.Distinct(StringComparer.Ordinal).Count() != field.Values.Count) ||
            enumFields.Select(field => (field.ModelKey, field.Field)).Distinct().Count() != enumFields.Length)
            throw new InvalidOperationException("DATA_SCOPE_CONDITION_REGISTRATION_CONFLICT");
    }

    /// <summary>保存前递归校验结构、直接字段、操作符、值和业务目标。</summary>
    public void Validate(string resourceKey, DataScopeConditionDto condition, string path = "1")
    {
        if (condition is null) throw Invalid("条件不能为空。", "DATA_SCOPE_CONDITION_REQUIRED");
        try
        {
            switch (condition.Type)
            {
                case "group":
                    if (condition.Operator is not ("and" or "or") || condition.Children is null || condition.Children.Count == 0 ||
                        condition.Field != "" || condition.Key is not null || condition.ContextKey is not null || HasValue(condition.Value))
                        throw Invalid("AND／OR 组合结构无效。", "DATA_SCOPE_GROUP_INVALID");
                    for (var index = 0; index < condition.Children.Count; index++) Validate(resourceKey, condition.Children[index], $"{path}.{index + 1}");
                    return;
                case "business":
                    if (condition.Children is not null || condition.ContextKey is not null || condition.Field != "" || condition.Operator != "")
                        throw Invalid("业务条件结构无效。", "DATA_SCOPE_BUSINESS_STRUCTURE_INVALID");
                    var (provider, definition) = Business(condition.Key, resourceKey);
                    try { provider.Validate(definition.Key, condition.Value); }
                    catch (ArgumentException exception) { throw Invalid(exception.Message, "DATA_SCOPE_BUSINESS_VALUE_INVALID"); }
                    return;
                case "attribute":
                    if (condition.Children is not null || condition.Key is not null) throw Invalid("属性条件结构无效。", "DATA_SCOPE_ATTRIBUTE_STRUCTURE_INVALID");
                    var property = Property(resourceKey, condition.Field);
                    var valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    var field = Field(resourceKey, condition.Field);
                    var operation = condition.Operator;
                    if (operation is "isNull" or "notNull")
                    {
                        if (condition.ContextKey is not null || HasValue(condition.Value)) throw Invalid("空值操作符不接受比较值。", "DATA_SCOPE_NULL_COMPARISON_VALUE");
                        return;
                    }
                    if (operation is not ("equal" or "notEqual" or "greaterThan" or "greaterThanOrEqual" or "lessThan" or "lessThanOrEqual"
                        or "contains" or "startsWith" or "endsWith" or "in" or "notIn")) throw Invalid("条件操作符不支持。", "DATA_SCOPE_OPERATOR_UNKNOWN");
                    if (operation is "contains" or "startsWith" or "endsWith" && (valueType != typeof(string) || field.ValueType == FieldValueType.Enum) ||
                        operation is "greaterThan" or "greaterThanOrEqual" or "lessThan" or "lessThanOrEqual"
                            && valueType != typeof(long) && valueType != typeof(DateOnly) && valueType != typeof(DateTime))
                        throw Invalid("操作符与属性类型不相容。", "DATA_SCOPE_OPERATOR_TYPE_MISMATCH");
                    if (condition.ContextKey is not null)
                    {
                        if (operation is "in" or "notIn" || HasValue(condition.Value)) throw Invalid("当前值条件结构无效。", "DATA_SCOPE_CONTEXT_STRUCTURE_INVALID");
                        var context = identities.ContextValues.SingleOrDefault(value => value.Key == condition.ContextKey)
                            ?? throw Invalid("当前值未登记。", "DATA_SCOPE_CONTEXT_UNKNOWN");
                        if (context.ValueType != valueType.Name || !identities.ContextBindings.Any(binding =>
                            binding.ModelKey == catalog.Resolve(resourceKey).ModelKey && binding.Field == property.Name && binding.ContextKey == context.Key))
                            throw Invalid("当前值与属性的规范身份域不相容。", "DATA_SCOPE_CONTEXT_TYPE_MISMATCH");
                        return;
                    }
                    if (operation is "in" or "notIn")
                    {
                        if (condition.Value.ValueKind != JsonValueKind.Array) throw Invalid("集合比较需要数组值。", "DATA_SCOPE_ARRAY_REQUIRED");
                        foreach (var item in condition.Value.EnumerateArray())
                        {
                            _ = Parse(item, valueType, field.ValueType);
                            ValidateEnum(resourceKey, condition.Field, field.ValueType, item);
                        }
                    }
                    else
                    {
                        _ = Parse(condition.Value, valueType, field.ValueType);
                        ValidateEnum(resourceKey, condition.Field, field.ValueType, condition.Value);
                    }
                    return;
                default: throw Invalid("条件类型未登记。", "DATA_SCOPE_CONDITION_TYPE_UNKNOWN");
            }
        }
        catch (RolePermissionValidationException exception) when (!exception.Args.ContainsKey("conditionPath"))
        { throw new RolePermissionValidationException($"条件 {path}：{exception.Message}", exception.Code,
            new Dictionary<string, object?>(exception.Args) { ["conditionPath"] = path, ["field"] = condition.Field, ["operator"] = condition.Operator }, "condition"); }
    }

    /// <summary>只对固定日期叶子调用正式规范器；已保存 UTC 再保存仍保持原时点。</summary>
    public DataScopeConditionDto Normalize(string resourceKey, DataScopeConditionDto condition, string path = "1")
    {
        try
        {
            if (condition.Type == "group") return condition with
            {
                Value = JsonSerializer.SerializeToElement((object?)null),
                Children = condition.Children!
                .Select((child, index) => Normalize(resourceKey, child, $"{path}.{index + 1}")).ToArray()
            };
            if (condition.Type != "attribute" || condition.ContextKey is not null || condition.Operator is "isNull" or "notNull")
                return condition with
                {
                    Value = condition.Value.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement((object?)null) : condition.Value
                };
            var field = Field(resourceKey, condition.Field);
            if (field.ValueType == FieldValueType.Integer)
            {
                return condition with
                {
                    Value = condition.Operator is "in" or "notIn"
                    ? JsonSerializer.SerializeToElement(condition.Value.EnumerateArray().Select(item => (long)Parse(item, typeof(long), field.ValueType)).ToArray())
                    : JsonSerializer.SerializeToElement((long)Parse(condition.Value, typeof(long), field.ValueType))
                };
            }
            if (field.ValueType is not (FieldValueType.Instant or FieldValueType.CalendarDate)) return condition;
            if (temporal is null) throw new InvalidOperationException("DATA_SCOPE_TEMPORAL_NORMALIZER_REQUIRED");
            // 同一规范器处理单值和集合成员，避免保存后按查看者时区重新解释。
            JsonElement NormalizeValue(JsonElement value)
            {
                var result = temporal.Normalize(field.ValueType, value);
                if (!result.IsSuccess) throw Invalid(result.Message ?? "日期比较值无效。", "DATA_SCOPE_" + result.ErrorCode);
                return JsonSerializer.SerializeToElement(result.Value);
            }
            return condition with
            {
                Value = condition.Operator is "in" or "notIn"
                ? JsonSerializer.SerializeToElement(condition.Value.EnumerateArray().Select(NormalizeValue).ToArray())
                : NormalizeValue(condition.Value)
            };
        }
        catch (RolePermissionValidationException exception) when (!exception.Args.ContainsKey("conditionPath"))
        { throw new RolePermissionValidationException($"条件 {path}：{exception.Message}", exception.Code,
            new Dictionary<string, object?>(exception.Args) { ["conditionPath"] = path, ["field"] = condition.Field, ["operator"] = condition.Operator }, "condition"); }
    }

    /// <summary>缺失读取叶子被移除，AND／OR 仅组合剩余贡献；null 表示整节点缺失。</summary>
    public Expression? BuildRead(string resourceKey, DataScopeConditionDto condition, ParameterExpression row,
        DataScopeResolvedIdentities facts) => Build(resourceKey, condition, row, facts, null);

    /// <summary>在解析主体事实前识别整节点是否具有读取贡献，避免无贡献节点触发无关依赖。</summary>
    public bool HasReadContribution(string resourceKey, DataScopeConditionDto condition) => condition.Type switch
    {
        "attribute" => true,
        "business" => Business(condition.Key, resourceKey).Definition.HasReadContribution,
        "group" => condition.Children!.Any(child => HasReadContribution(resourceKey, child)),
        _ => throw new InvalidOperationException("DATA_SCOPE_CONDITION_INVALID")
    };

    /// <summary>完整业务判断先取 owner 事实，属性部分仍交数据库比较，保持单对象与 SQL 排序规则一致。</summary>
    public async Task<bool> MatchesAsync(string resourceKey, string objectId, DataScopeConditionDto condition,
        DataScopeResolvedIdentities facts, CancellationToken token)
    {
        var resource = catalog.Resolve(resourceKey);
        var decisions = new Dictionary<DataScopeConditionDto, bool>(ReferenceEqualityComparer.Instance);
        await ResolveBusinessAsync(condition, resource, objectId, decisions, token);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var predicate = Build(resourceKey, condition, row, facts, decisions)
            ?? throw new InvalidOperationException("DATA_SCOPE_FULL_CONDITION_UNAVAILABLE");
        var id = Expression.Equal(Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], row,
            Expression.Constant(resource.IdField)), Expression.Constant(objectId));
        var query = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking();
        return await query.AnyAsync(Expression.Lambda<Func<Dictionary<string, object?>, bool>>(Expression.AndAlso(id, predicate), row), token);
    }

    /// <summary>用参数化的单行 SQL 候选复用数据库属性比较规则；业务叶子直接消费待写事实。</summary>
    public async Task<bool> MatchesCandidateAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
        DataScopeConditionDto condition, DataScopeResolvedIdentities facts, CancellationToken token)
    {
        Validate(resourceKey, condition);
        var resource = catalog.Resolve(resourceKey);
        var decisions = new Dictionary<DataScopeConditionDto, bool>(ReferenceEqualityComparer.Instance);
        await ResolveBusinessCandidateAsync(condition, resource, candidate, decisions, token);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var predicate = Build(resourceKey, condition, row, facts, decisions)
            ?? throw new InvalidOperationException("DATA_SCOPE_FULL_CONDITION_UNAVAILABLE");
        var model = db.Model.FindEntityType(db.ResolveModelKey(resource.ModelKey))
            ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
        var columns = model.GetProperties().ToArray();
        var parameters = new object[columns.Length];
        var selections = new string[columns.Length];
        for (var index = 0; index < columns.Length; index++)
        {
            var property = columns[index];
            candidate.TryGetValue(property.Name, out var value);
            parameters[index] = value ?? DBNull.Value;
            var column = property.GetColumnName()!.Replace("]", "]]", StringComparison.Ordinal);
            selections[index] = $"CAST({{{index}}} AS {property.GetColumnType() ?? property.GetRelationalTypeMapping().StoreType}) AS [{column}]";
        }
        var sql = "SELECT " + string.Join(", ", selections);
        var query = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey))
            .FromSqlRaw(sql, parameters).AsNoTracking();
        return await query.AnyAsync(Expression.Lambda<Func<Dictionary<string, object?>, bool>>(predicate, row), token);
    }

    /// <summary>当前值解析只选择实际引用键，组合树的每个叶子仍共用一次主体事实。</summary>
    public IEnumerable<string> ContextKeys(DataScopeConditionDto condition) => condition.Type == "group"
        ? condition.Children!.SelectMany(ContextKeys) : condition.ContextKey is null ? [] : [condition.ContextKey];

    // 完整判断只调用条件所属模块的事实源，异常直接传播。
    private async Task ResolveBusinessAsync(DataScopeConditionDto condition, DataScopeResource resource, string objectId,
        Dictionary<DataScopeConditionDto, bool> decisions, CancellationToken token)
    {
        if (condition.Type == "group")
        {
            foreach (var child in condition.Children!) await ResolveBusinessAsync(child, resource, objectId, decisions, token);
        }
        else if (condition.Type == "business")
        {
            var (provider, definition) = Business(condition.Key, resource.ResourceKey);
            decisions[condition] = await provider.MatchesAsync(definition.Key, resource, objectId, condition.Value, token);
        }
    }

    // 新建尚无目标行，业务条件只能读取候选及其所属模块的既有事实。
    private async Task ResolveBusinessCandidateAsync(DataScopeConditionDto condition, DataScopeResource resource,
        IReadOnlyDictionary<string, object?> candidate, Dictionary<DataScopeConditionDto, bool> decisions, CancellationToken token)
    {
        if (condition.Type == "group")
        {
            foreach (var child in condition.Children!)
                await ResolveBusinessCandidateAsync(child, resource, candidate, decisions, token);
        }
        else if (condition.Type == "business")
        {
            var (provider, definition) = Business(condition.Key, resource.ResourceKey);
            decisions[condition] = await provider.MatchesCandidateAsync(definition.Key, resource, candidate, condition.Value, token);
        }
    }

    // 同一递归结构构造完整谓词和读取投影；缺失读取叶子返回 null。
    private Expression? Build(string resourceKey, DataScopeConditionDto condition, ParameterExpression row,
        DataScopeResolvedIdentities facts, Dictionary<DataScopeConditionDto, bool>? decisions)
    {
        if (condition.Type == "group")
        {
            Expression? result = null;
            foreach (var child in condition.Children!)
            {
                var part = Build(resourceKey, child, row, facts, decisions);
                if (part is null) continue;
                result = result is null ? part : condition.Operator == "and" ? Expression.AndAlso(result, part) : Expression.OrElse(result, part);
            }
            return result;
        }
        if (condition.Type == "business")
        {
            if (decisions is not null) return Expression.Constant(decisions[condition]);
            var (provider, definition) = Business(condition.Key, resourceKey);
            return definition.HasReadContribution ? provider.BuildReadExpression(definition.Key, catalog.Resolve(resourceKey), row, condition.Value) : null;
        }
        var property = Property(resourceKey, condition.Field);
        var field = Field(resourceKey, condition.Field);
        var access = Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], row, Expression.Constant(property.Name));
        // EF 的非空值类型不能创建 null 常量；显式空值操作符在此直接得到确定结果。
        var nullValue = property.IsNullable ? Expression.Constant(null, property.ClrType) : null;
        if (condition.Operator == "isNull") return nullValue is null ? Expression.Constant(false) : Expression.Equal(access, nullValue);
        if (condition.Operator == "notNull") return nullValue is null ? Expression.Constant(true) : Expression.NotEqual(access, nullValue);
        object? value = condition.ContextKey is null ? null : facts.ContextValue(condition.ContextKey);
        if (condition.ContextKey is not null && value is null) return Expression.Constant(false);
        var valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        Expression comparison;
        if (condition.Operator is "in" or "notIn")
        {
            var values = condition.Value.EnumerateArray().Select(item => Parse(item, valueType, field.ValueType)).ToArray();
            var array = Array.CreateInstance(property.ClrType, values.Length);
            for (var index = 0; index < values.Length; index++) array.SetValue(values[index], index);
            comparison = Expression.Call(ContainsMethod.MakeGenericMethod(property.ClrType), Expression.Constant(array), access);
            if (condition.Operator == "notIn") comparison = Expression.Not(comparison);
        }
        else
        {
            value ??= Parse(condition.Value, valueType, field.ValueType);
            var constant = Expression.Convert(Expression.Constant(value, valueType), property.ClrType);
            comparison = condition.Operator switch
            {
                "equal" => Expression.Equal(access, constant),
                "notEqual" => Expression.NotEqual(access, constant),
                "greaterThan" => Expression.GreaterThan(access, constant),
                "greaterThanOrEqual" => Expression.GreaterThanOrEqual(access, constant),
                "lessThan" => Expression.LessThan(access, constant),
                "lessThanOrEqual" => Expression.LessThanOrEqual(access, constant),
                "contains" => Expression.Call(access, nameof(string.Contains), [], Expression.Constant((string)value)),
                "startsWith" => Expression.Call(access, nameof(string.StartsWith), [], Expression.Constant((string)value)),
                "endsWith" => Expression.Call(access, nameof(string.EndsWith), [], Expression.Constant((string)value)),
                _ => throw new InvalidOperationException("DATA_SCOPE_OPERATOR_UNAVAILABLE")
            };
        }
        // SQL 三值逻辑下，所有非 null 专用操作符均要求实际字段非空。
        return nullValue is not null ? Expression.AndAlso(Expression.NotEqual(access, nullValue), comparison) : comparison;
    }

    // 标识与当前目标须同时匹配，不能凭业务条件键猜测适用对象。
    private (IDataScopeBusinessConditionProvider Provider, DataScopeBusinessConditionDefinition Definition) Business(string? key, string resourceKey)
    {
        var matches = providers.SelectMany(provider => provider.Definitions.Select(definition => (Provider: provider, Definition: definition)))
            .Where(item => item.Definition.Key == key).ToArray();
        if (matches.Length != 1 || !matches[0].Definition.ModelKeys.Contains(catalog.Resolve(resourceKey).ModelKey, StringComparer.Ordinal))
            throw Invalid("业务条件未登记或目标不相容。", "DATA_SCOPE_BUSINESS_TARGET_INVALID");
        return matches[0];
    }

    // 仅从正式资源所指模型选择直接原始字段。
    private IProperty Property(string resourceKey, string field) => catalog.Fields(resourceKey).SingleOrDefault(item => item.Name == field)
        ?? throw Invalid("条件属性不存在。", "DATA_SCOPE_FIELD_UNKNOWN");

    // 逻辑值类型来自同一持久化模型定义，而非界面猜测。
    private ICP.Platform.Modeling.Definitions.FieldDefinition Field(string resourceKey, string field) => db.PersistencePlan.Models
        .Single(model => model.LogicalKey == catalog.Resolve(resourceKey).ModelKey).Fields.Single(item => item.Name == field);

    // 枚举字符串必须属于字段 owner 登记的实际持久化值。
    private void ValidateEnum(string resourceKey, string field, FieldValueType kind, JsonElement value)
    {
        if (kind != FieldValueType.Enum) return;
        var values = EnumValues(resourceKey, field);
        if (values is null || !values.Contains(value.GetString()!, StringComparer.Ordinal)) throw Invalid("枚举比较值未登记。", "DATA_SCOPE_ENUM_UNKNOWN");
    }

    // 标量和值集合共用严格解析；UI 的大整数文本在保存时转换为精确 long。
    private static object Parse(JsonElement value, Type type, FieldValueType kind)
    {
        if (!HasValue(value)) throw Invalid("比较值不能为空；空值请使用专门操作符。", "DATA_SCOPE_VALUE_REQUIRED");
        try
        {
            if (kind == FieldValueType.Enum && value.ValueKind != JsonValueKind.String) throw new JsonException();
            if (kind == FieldValueType.Integer && type == typeof(long) && value.ValueKind == JsonValueKind.String)
                return long.Parse(value.GetString()!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return value.Deserialize(type) ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException or OverflowException)
        { throw Invalid("比较值与属性类型不相容。", "DATA_SCOPE_VALUE_TYPE_MISMATCH"); }
    }

    // null 与缺少值都不是普通比较值。
    private static bool HasValue(JsonElement value) => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    // 保存边界将配置错误映射为现有 Problem Details。
    private static RolePermissionValidationException Invalid(string message, string code) => new(message, code, field: "condition");
}
