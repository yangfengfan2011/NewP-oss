using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Runtime;

public static class StandardCrudDescriptorValidator
{
    /// <summary>
    /// 校验标准 CRUD 描述符的静态边界，失败信息作为测试基线保持稳定。
    /// </summary>
    public static void Validate(StandardCrudDescriptor descriptor)
    {
        Require(descriptor.ResourceKey, nameof(descriptor.ResourceKey), descriptor.ResourceKey);
        Require(descriptor.ModuleId, nameof(descriptor.ModuleId), descriptor.ResourceKey);
        Require(descriptor.ModelName, nameof(descriptor.ModelName), descriptor.ResourceKey);
        Require(descriptor.Route, nameof(descriptor.Route), descriptor.ResourceKey);
        Require(descriptor.IdField, nameof(descriptor.IdField), descriptor.ResourceKey);
        Require(descriptor.DisplayField, nameof(descriptor.DisplayField), descriptor.ResourceKey);

        if (!descriptor.FieldSet.DetailFields.Contains(descriptor.IdField, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的 DetailFields 必须包含 IdField。");
        }

        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Conditional && descriptor.ResourceKey.Length == 0)
        {
            throw new InvalidOperationException("标准 CRUD 条件删除资源必须声明 ResourceKey。");
        }

        ValidateDisplayValueFields(descriptor);
        ValidateSort(descriptor);
        ValidateRequiredFields(descriptor);
        ValidateUniqueConstraints(descriptor);
    }

    // 这里统一生成描述符失败信号，避免后续模块各自散落不同错误文案。
    private static void Require(string value, string name, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 缺少 {name}。");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    // display value 只描述结构化引用展示字段；source/target 元数据必须在这里静态收口，避免后续接入层各自兜底。
    private static void ValidateDisplayValueFields(StandardCrudDescriptor descriptor)
    {
        foreach (var displayField in descriptor.DisplayValueFields)
        {
            Require(
                !string.IsNullOrWhiteSpace(displayField.Field),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 存在空 display value 字段。");
            Require(
                !string.IsNullOrWhiteSpace(displayField.SourceField),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明 source 字段。");
            Require(
                descriptor.FieldSet.ListFields.Contains(displayField.SourceField, StringComparer.Ordinal),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 引用了未纳入列表字段白名单的 source 字段 {displayField.SourceField}。");
            Require(
                !descriptor.FieldSet.ListFields.Contains(displayField.Field, StringComparer.Ordinal),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 不能同时作为真实列表字段声明。");
            Require(
                !string.IsNullOrWhiteSpace(displayField.TargetModelName),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标模型。");
            Require(
                !string.IsNullOrWhiteSpace(displayField.TargetIdField),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标 id 字段。");
            Require(
                !string.IsNullOrWhiteSpace(displayField.TargetDisplayField),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标显示字段。");
        }
    }

    // 排序字段必须停留在真实列表字段内，默认排序也必须属于显式白名单。
    private static void ValidateSort(StandardCrudDescriptor descriptor)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in descriptor.SortableFields)
        {
            Require(!string.IsNullOrWhiteSpace(field), $"标准 CRUD 资源 {descriptor.ResourceKey} 的排序字段不能为空。");
            Require(names.Add(field), $"标准 CRUD 资源 {descriptor.ResourceKey} 的排序字段 {field} 重复。");
            Require(
                descriptor.FieldSet.ListFields.Contains(field, StringComparer.Ordinal),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的排序字段 {field} 未纳入列表字段白名单。");
        }

        if (!string.IsNullOrWhiteSpace(descriptor.DefaultSortField))
        {
            Require(
                names.Contains(descriptor.DefaultSortField),
                $"标准 CRUD 资源 {descriptor.ResourceKey} 的默认排序字段 {descriptor.DefaultSortField} 未纳入排序白名单。");
        }

        Require(
            descriptor.DefaultSortDirection is "asc" or "desc",
            $"标准 CRUD 资源 {descriptor.ResourceKey} 的默认排序方向 {descriptor.DefaultSortDirection} 无效。");
    }

    // 必填声明只做静态边界校验：字段归属和错误契约必须清晰，运行时缺失判断由服务内核统一接管。
    private static void ValidateRequiredFields(StandardCrudDescriptor descriptor)
    {
        var declarations = new HashSet<(string Field, StandardCrudRequiredFieldScope Scope)>();
        var declaredFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.FieldSet.DetailFields)
            .Concat(descriptor.FieldSet.CreateFields)
            .Concat(descriptor.FieldSet.UpdateFields)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var requiredField in descriptor.RequiredFields)
        {
            RequireRequiredFieldValue(requiredField.Field, nameof(requiredField.Field), descriptor.ResourceKey);
            RequireRequiredFieldValue(requiredField.ErrorCode, nameof(requiredField.ErrorCode), descriptor.ResourceKey, requiredField.Field);
            RequireRequiredFieldValue(requiredField.Message, nameof(requiredField.Message), descriptor.ResourceKey, requiredField.Field);
            RequireRequiredFieldValue(requiredField.ReasonKey, nameof(requiredField.ReasonKey), descriptor.ResourceKey, requiredField.Field);

            if (!declarations.Add((requiredField.Field, requiredField.Scope)))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 在 {requiredField.Scope} 范围内重复声明。");
            }

            if (!declaredFields.Contains(requiredField.Field))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 未在字段集合中声明。");
            }

            if (!descriptor.FieldSet.DetailFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 DetailFields 中。");
            }

            if (RequiresCreate(requiredField.Scope)
                && !descriptor.FieldSet.CreateFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 CreateFields 中。");
            }

            if (RequiresUpdate(requiredField.Scope)
                && !descriptor.FieldSet.UpdateFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 UpdateFields 中。");
            }
        }
    }

    private static bool RequiresCreate(StandardCrudRequiredFieldScope scope) =>
        scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;

    private static bool RequiresUpdate(StandardCrudRequiredFieldScope scope) =>
        scope is StandardCrudRequiredFieldScope.UpdateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;

    private static void RequireRequiredFieldValue(string value, string name, string resourceKey, string? field = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var fieldPart = string.IsNullOrWhiteSpace(field) ? string.Empty : $" {field}";
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 的必填字段{fieldPart} 缺少 {name}。");
        }
    }

    // 唯一约束只做静态边界校验：字段归属必须清晰，运行时查重由服务内核统一接管。
    private static void ValidateUniqueConstraints(StandardCrudDescriptor descriptor)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var declaredFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.FieldSet.DetailFields)
            .Concat(descriptor.FieldSet.CreateFields)
            .Concat(descriptor.FieldSet.UpdateFields)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var constraint in descriptor.UniqueConstraints)
        {
            RequireUniqueConstraintValue(constraint.Key, nameof(constraint.Key), descriptor.ResourceKey);
            RequireUniqueConstraintValue(constraint.ErrorCode, nameof(constraint.ErrorCode), descriptor.ResourceKey, constraint.Key);
            RequireUniqueConstraintValue(constraint.Message, nameof(constraint.Message), descriptor.ResourceKey, constraint.Key);
            RequireUniqueConstraintValue(constraint.ReasonKey, nameof(constraint.ReasonKey), descriptor.ResourceKey, constraint.Key);

            if (!keys.Add(constraint.Key))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 key {constraint.Key} 重复。");
            }

            if (constraint.Fields.Count == 0)
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 必须声明 Fields。");
            }

            ValidateUniqueFieldList(descriptor.ResourceKey, constraint.Key, nameof(constraint.Fields), constraint.Fields);
            ValidateUniqueFieldList(descriptor.ResourceKey, constraint.Key, nameof(constraint.ScopeFields), constraint.ScopeFields);

            foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
            {
                if (string.Equals(field, descriptor.IdField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 不能包含 IdField。");
                }

                if (!declaredFields.Contains(field))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 未在字段集合中声明。");
                }

                if (!descriptor.FieldSet.DetailFields.Contains(field, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 必须包含在 DetailFields 中。");
                }
            }
        }
    }

    private static void ValidateUniqueFieldList(
        string resourceKey,
        string constraintKey,
        string listName,
        IReadOnlyList<string> fields)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 的唯一约束 {constraintKey} 字段不能为空。");
            }

            if (!names.Add(field))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 的唯一约束 {constraintKey} {listName} 字段 {field} 重复。");
            }
        }
    }

    private static void RequireUniqueConstraintValue(string value, string name, string resourceKey, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var keyPart = string.IsNullOrWhiteSpace(key) ? string.Empty : $" {key}";
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 的唯一约束{keyPart}缺少 {name}。");
        }
    }
}
