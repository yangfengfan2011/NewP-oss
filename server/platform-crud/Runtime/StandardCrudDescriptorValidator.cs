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

            foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
            {
                if (string.Equals(field, descriptor.IdField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 不得包含 IdField。");
                }

                if (!declaredFields.Contains(field))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 必须在 ListFields、DetailFields、CreateFields 或 UpdateFields 中声明。");
                }

                if (!descriptor.FieldSet.DetailFields.Contains(field, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 必须包含在 DetailFields 中。");
                }
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
