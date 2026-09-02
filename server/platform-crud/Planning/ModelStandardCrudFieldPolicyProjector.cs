using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Crud.Planning;

/// <summary>
/// 把 Model 的存储和表单 authoring 投影为 Standard CRUD 规范字段策略。
/// </summary>
public sealed class ModelStandardCrudFieldPolicyProjector
{
    /// <summary>
    /// 为当前资源详情字段投影存储、输入、所有权和创建默认值，不从错误展示契约反推必填语义。
    /// </summary>
    public IReadOnlyList<StandardCrudFieldPolicy> Project(
        ModelMetadataDocument document,
        ModelDefinition model,
        StandardCrudFieldSet fieldSet)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fieldSet);

        if (!string.Equals(document.Model.Name, model.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model authoring '{document.Model.Name}' does not match artifact '{model.Name}'.");
        }

        var metadataByField = document.Model.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        var detailFields = fieldSet.DetailFields.ToHashSet(StringComparer.Ordinal);
        return model.Fields
            .Where(field => detailFields.Contains(field.Name))
            .Select(field => ProjectField(field, metadataByField.GetValueOrDefault(field.Name)))
            .ToArray();
    }

    // 固定主键和审计字段没有调用方表单 authoring，其值所有权由平台内建模型语义决定。
    private static StandardCrudFieldPolicy ProjectField(
        FieldDefinition field,
        ModelMetadataField? metadata)
    {
        var serverOwned = field.IsAuditField || field.IsPrimaryKey && metadata is null;
        var isShortText = field.ValueType == FieldValueType.Text;
        return new StandardCrudFieldPolicy
        {
            Field = field.Name,
            ValueType = field.ValueType,
            StorageRequired = field.Required,
            CreateInputRequired = !serverOwned && metadata?.Form?.Create?.Required == true,
            UpdateNonEmpty = !serverOwned && metadata?.Form?.Edit?.Required == true,
            ValueOwner = serverOwned ? StandardCrudFieldValueOwner.Server : StandardCrudFieldValueOwner.Caller,
            CreateDefaultValue = serverOwned ? null : ReadDefaultValue(metadata?.Form?.Create?.DefaultValue),
            Trim = !serverOwned && isShortText,
            MaxLength = !serverOwned && isShortText ? 256 : null,
            EmptyAsNull = !serverOwned && isShortText && metadata?.Storage?.Nullable == true
        };
    }

    // JsonElement 只接受确定的标量值，避免把对象或数组误变成可执行的应用默认逻辑。
    private static object? ReadDefaultValue(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number when element.Value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.Value.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException("Standard CRUD create default must be a scalar JSON value.")
        };
    }
}
