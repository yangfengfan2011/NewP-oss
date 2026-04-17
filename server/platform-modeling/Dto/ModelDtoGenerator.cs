using System.Text;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modeling.Dto;

public sealed class ModelDtoGenerator
{
    /// <summary>
    /// 将模型定义转换为单个 DTO 描述；引用字段和枚举字段会追加展示值属性。
    /// </summary>
    public ModelDtoDescriptor Describe(ModelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var properties = new List<ModelDtoProperty>();
        foreach (var field in definition.Fields)
        {
            properties.Add(new ModelDtoProperty(
                field.Name,
                ResolvePropertyType(field),
                !field.Required));

            // 参考字段和枚举字段需要同时保留展示值，避免 UI 再维护一套补充 DTO。
            if (IsReferenceOrEnum(field))
            {
                properties.Add(new ModelDtoProperty(field.Name + "_DisplayValue", "string", true));
            }
        }

        return new ModelDtoDescriptor(definition.Name, properties);
    }

    /// <summary>
    /// 渲染 DTO 源码，当前只生成单个类，不拆分输入/输出模型。
    /// </summary>
    public string Generate(ModelDefinition definition)
    {
        var descriptor = Describe(definition);
        var builder = new StringBuilder();

        builder.AppendLine("public sealed class " + descriptor.Name + "Dto");
        builder.AppendLine("{");

        foreach (var property in descriptor.Properties)
        {
            builder.AppendLine(
                $"    public {property.TypeName}{(property.Nullable ? "?" : string.Empty)} {property.PropertyName} {{ get; init; }}");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static bool IsReferenceOrEnum(FieldDefinition field)
    {
        return field.Kind == FieldKind.Reference || field.ValueType == FieldValueType.Enum;
    }

    private static string ResolvePropertyType(FieldDefinition field)
    {
        return field.ValueType switch
        {
            FieldValueType.Boolean => "bool",
            FieldValueType.DateTime => "DateTime",
            FieldValueType.ObjectId => ResolveObjectIdType(field.StoreType),
            FieldValueType.Enum => "string",
            _ => "string"
        };
    }

    private static string ResolveObjectIdType(string storeType)
    {
        return storeType.Equals("guid", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("uniqueidentifier", StringComparison.OrdinalIgnoreCase)
            ? "Guid"
            : "string";
    }
}
