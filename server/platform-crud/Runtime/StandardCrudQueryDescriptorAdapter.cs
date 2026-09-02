using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Modeling.DisplayPlanning;

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

        var displayValueFields = GetQueryDisplayValueFields(descriptor);
        var defaultFields = descriptor.FieldSet.ListFields
            .Concat(displayValueFields.Select(static field => field.Field))
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
            SortableFields = descriptor.SortableFields,
            DefaultSortField = descriptor.DefaultSortField,
            DefaultSortDirection = descriptor.DefaultSortDirection,
            DisplayValueFields = displayValueFields
        };
    }

    /// <summary>把 lookup 与 temporal companion 合并为查询层只读响应字段。</summary>
    public static IReadOnlyList<PlatformDisplayValueField> GetQueryDisplayValueFields(StandardCrudDescriptor descriptor)
    {
        var fields = descriptor.DisplayValueFields.ToList();
        if (descriptor.DisplayProjectionPlan is null)
        {
            return fields;
        }

        foreach (var plan in descriptor.DisplayProjectionPlan.Fields
                     .Where(static plan => plan is TemporalFieldDisplayProjectionPlan or CalendarDateFieldDisplayProjectionPlan))
        {
            if (fields.Any(field => string.Equals(field.Field, plan.CompanionField, StringComparison.Ordinal)))
            {
                continue;
            }

            fields.Add(new PlatformDisplayValueField(
                plan.CompanionField,
                plan.SourceField,
                "$temporal",
                plan.SourceField,
                plan.CompanionField));
        }

        return fields;
    }
}
