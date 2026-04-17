using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    public static readonly ModelDefinition ValueListItem = new(
        "ValueListItem",
        "ValueListItem",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "Description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_ValueListItem_Code", new[] { "Code" }, true)
        });

    private static readonly Lazy<ModelDefinition> ConfigItemDefinition = new(() =>
        // ConfigItem 先通过元数据 provider 进入新链路，ValueListItem 仍保留现有手写定义。
        new ConfigCenterModelMetadataProvider().LoadConfigItem());

    public static ModelDefinition ConfigItem => ConfigItemDefinition.Value;

    public static readonly IReadOnlyList<ModelDefinition> All = new[]
    {
        ValueListItem,
        ConfigItem
    };
}
