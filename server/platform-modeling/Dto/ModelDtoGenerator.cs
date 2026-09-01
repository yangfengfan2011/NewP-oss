using System.Text;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.DisplayPlanning;

namespace ICP.Platform.Modeling.Dto;

public sealed class ModelDtoGenerator
{
    /// <summary>
    /// 将模型定义和已编译 display plan 转换为单个 DTO 描述。
    /// </summary>
    /// <param name="definition">DTO 原始字段与类型的规范 Model definition。</param>
    /// <param name="displayProjectionPlan">同一 Model 通过统一 planner 成功编译的 display plan。</param>
    /// <returns>包含 raw 属性与计划内 output-only companion 的 DTO 描述。</returns>
    public ModelDtoDescriptor Describe(
        ModelDefinition definition,
        ModelDisplayProjectionPlan displayProjectionPlan)
    {
        return Describe(definition, displayProjectionPlan, null);
    }

    /// <summary>
    /// 将模型定义和已编译 display plan 转换为单个 DTO 描述；枚举字段可通过外部映射改用强类型 CLR 名称。
    /// </summary>
    /// <param name="definition">DTO 原始字段与类型的规范 Model definition。</param>
    /// <param name="displayProjectionPlan">同一 Model 通过统一 planner 成功编译的 display plan。</param>
    /// <param name="enumClrTypeMap">可选的 enum 字段到 CLR 类型名映射。</param>
    /// <returns>包含 raw 属性与计划内 output-only companion 的 DTO 描述。</returns>
    public ModelDtoDescriptor Describe(
        ModelDefinition definition,
        ModelDisplayProjectionPlan displayProjectionPlan,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(displayProjectionPlan);
        if (!string.Equals(definition.Name, displayProjectionPlan.ModelName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"DTO model '{definition.Name}' cannot consume display projection plan for '{displayProjectionPlan.ModelName}'.",
                nameof(displayProjectionPlan));
        }

        var displayPlansBySourceField = displayProjectionPlan.Fields.ToDictionary(
            static plan => plan.SourceField,
            StringComparer.Ordinal);

        var properties = new List<ModelDtoProperty>();
        foreach (var field in definition.Fields)
        {
            var typeName = ResolvePropertyType(field, enumClrTypeMap);
            properties.Add(new ModelDtoProperty(
                ResolvePropertyName(field),
                typeName,
                !field.Required,
                ResolvePropertyInitializer(field, typeName)));

            // companion 资格和规范名称只来自 planner，生成器不再按字段类型推导第二套协议。
            if (displayPlansBySourceField.TryGetValue(field.Name, out var displayPlan))
            {
                properties.Add(new ModelDtoProperty(displayPlan.CompanionField, "string", true));
            }
        }

        return new ModelDtoDescriptor(definition.Name, properties);
    }

    /// <summary>
    /// 渲染 DTO 源码，display companion 仅来自已编译计划。
    /// </summary>
    /// <param name="definition">DTO 原始字段与类型的规范 Model definition。</param>
    /// <param name="displayProjectionPlan">同一 Model 通过统一 planner 成功编译的 display plan。</param>
    /// <returns>单个 DTO 类的稳定 C# 源码。</returns>
    public string Generate(
        ModelDefinition definition,
        ModelDisplayProjectionPlan displayProjectionPlan)
    {
        return Generate(definition, displayProjectionPlan, null);
    }

    /// <summary>
    /// 渲染 DTO 源码，枚举字段可按外部 CLR 类型映射输出强类型属性。
    /// </summary>
    /// <param name="definition">DTO 原始字段与类型的规范 Model definition。</param>
    /// <param name="displayProjectionPlan">同一 Model 通过统一 planner 成功编译的 display plan。</param>
    /// <param name="enumClrTypeMap">可选的 enum 字段到 CLR 类型名映射。</param>
    /// <returns>单个 DTO 类的稳定 C# 源码。</returns>
    public string Generate(
        ModelDefinition definition,
        ModelDisplayProjectionPlan displayProjectionPlan,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        var descriptor = Describe(definition, displayProjectionPlan, enumClrTypeMap);
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

    // 将规范字段类型映射为受管 DTO 的稳定 CLR 类型名。
    private static string ResolvePropertyType(
        FieldDefinition field,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        return field.ValueType switch
        {
            FieldValueType.Boolean => "bool",
            FieldValueType.DateTime => "DateTime",
            FieldValueType.Instant => "DateTime",
            FieldValueType.Integer => ResolveIntegerType(field.StoreType),
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

    private static string ResolveIntegerType(string storeType)
    {
        return storeType.Equals("int", StringComparison.OrdinalIgnoreCase)
               || storeType.Equals("integer", StringComparison.OrdinalIgnoreCase)
            ? "int"
            : "long";
    }
}
