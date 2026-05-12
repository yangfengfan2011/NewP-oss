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
