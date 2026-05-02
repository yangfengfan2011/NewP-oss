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
    }

    // 这里统一生成描述符失败信号，避免后续模块各自散落不同错误文案。
    private static void Require(string value, string name, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 缺少 {name}。");
        }
    }
}
