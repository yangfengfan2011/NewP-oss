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
        return Describe(definition, null);
    }

    /// <summary>
    /// 将模型定义转换为单个 DTO 描述；枚举字段可通过外部映射改用强类型 CLR 名称。
    /// </summary>
    public ModelDtoDescriptor Describe(
        ModelDefinition definition,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var properties = new List<ModelDtoProperty>();
        foreach (var field in definition.Fields)
        {
            var typeName = ResolvePropertyType(field, enumClrTypeMap);
            properties.Add(new ModelDtoProperty(
                ResolvePropertyName(field),
                typeName,
                !field.Required,
                ResolvePropertyInitializer(field, typeName)));

            // 参考字段和枚举字段需要同时保留展示值，避免 UI 再维护一套补充 DTO。
            if (IsReferenceOrEnum(field))
            {
                properties.Add(new ModelDtoProperty(ResolveDisplayValuePropertyName(field), "string", true));
            }
        }

        return new ModelDtoDescriptor(definition.Name, properties);
    }

    /// <summary>
    /// 渲染 DTO 源码，当前只生成单个类，不拆分输入/输出模型。
    /// </summary>
    public string Generate(ModelDefinition definition)
    {
        return Generate(definition, null);
    }

    /// <summary>
    /// 渲染 DTO 源码，枚举字段可按外部 CLR 类型映射输出强类型属性。
    /// </summary>
    public string Generate(
        ModelDefinition definition,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        var descriptor = Describe(definition, enumClrTypeMap);
        var builder = new StringBuilder();

        builder.AppendLine("public sealed class " + descriptor.Name + "Dto");
        builder.AppendLine("{");

        foreach (var property in descriptor.Properties)
        {
            builder.AppendLine(
                $"    public {property.TypeName}{(property.Nullable ? "?" : string.Empty)} {property.PropertyName} {{ get; init; }}{property.Initializer}");

            if (string.Equals(property.PropertyName, "DisplayName", StringComparison.Ordinal)
                && string.Equals(property.TypeName, "string", StringComparison.Ordinal)
                && !property.Nullable)
            {
                // ENAME 是存储协议字段；Name 只作为 DTO 兼容别名，避免旧调用方在协议字段切换时立刻断裂。
                builder.AppendLine("    public string Name { get => DisplayName; init => DisplayName = value; }");
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static bool IsReferenceOrEnum(FieldDefinition field)
    {
        return field.Kind == FieldKind.Reference || field.ValueType == FieldValueType.Enum;
    }

    private static string ResolvePropertyName(FieldDefinition field)
    {
        return field.Name switch
        {
            "EID" => "Id",
            "ENAME" => "DisplayName",
            "ECODE" => "Code",
            _ => field.Name
        };
    }

    private static string ResolveDisplayValuePropertyName(FieldDefinition field)
    {
        return ResolvePropertyName(field) + "_DisplayValue";
    }

    private static string ResolvePropertyType(
        FieldDefinition field,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        return field.ValueType switch
        {
            FieldValueType.Boolean => "bool",
            FieldValueType.DateTime => "DateTime",
            FieldValueType.ObjectId => ResolveObjectIdType(field.StoreType),
            FieldValueType.Enum => ResolveEnumType(field, enumClrTypeMap),
            _ => "string"
        };
    }

    private static string? ResolvePropertyInitializer(FieldDefinition field, string typeName)
    {
        // 这些默认值是受管基础 DTO 的创建边界兼容约定：省略字段沿用旧手写 DTO 行为，显式赋值仍覆盖。
        if (field.ValueType == FieldValueType.Boolean
            && string.Equals(field.Name, "IsEnabled", StringComparison.Ordinal))
        {
            return " = true;";
        }

        if (field.ValueType == FieldValueType.Enum
            && string.Equals(field.Name, "EmploymentStatus", StringComparison.Ordinal)
            && field.Required
            && !string.Equals(typeName, "string", StringComparison.Ordinal))
        {
            return $" = ({typeName})1;";
        }

        return null;
    }

    private static string ResolveEnumType(
        FieldDefinition field,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        if (enumClrTypeMap is null)
        {
            return "string";
        }

        if (enumClrTypeMap.TryGetValue(field.Name, out var clrType)
            && !string.IsNullOrWhiteSpace(clrType))
        {
            return clrType;
        }

        var matchedEntry = enumClrTypeMap.FirstOrDefault(entry =>
            string.Equals(entry.Key, field.Name, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(matchedEntry.Value) ? "string" : matchedEntry.Value;
    }

    private static string ResolveObjectIdType(string storeType)
    {
        return storeType.Equals("guid", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("uniqueidentifier", StringComparison.OrdinalIgnoreCase)
            ? "Guid"
            : "string";
    }
}
