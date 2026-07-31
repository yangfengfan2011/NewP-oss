using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 将 Standard CRUD 资源描述符适配为平台查询 descriptor。
/// 这个适配层只负责边界映射，不负责请求解析、字段补齐或执行流程。
/// </summary>
public static class StandardCrudQueryDescriptorAdapter
{
    /// <summary>
    /// 将标准 CRUD 描述符转换为平台查询 descriptor。
    /// </summary>
    /// <param name="descriptor">标准 CRUD 资源描述符。</param>
    /// <returns>供平台查询内核使用的 descriptor。</returns>
    public static PlatformQueryDescriptor ToQueryDescriptor(StandardCrudDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var defaultFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.DisplayValueFields.Select(static field => field.Field))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new PlatformQueryDescriptor
        {
            ResourceKey = descriptor.ResourceKey,
            ModelName = descriptor.ModelName,
            IdField = descriptor.IdField,
            AllowedModelFields = descriptor.FieldSet.ListFields
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            DefaultFields = defaultFields,
            DefaultSortField = descriptor.DisplayField,
            DisplayValueFields = descriptor.DisplayValueFields
        };
    }
}
