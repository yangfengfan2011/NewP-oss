using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    public static readonly ModelDefinition ConfigItem = new(
        "ConfigItem",
        "config_items",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Key", "config_key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Value", "config_value", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("Type", "config_type", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("ModuleId", "module_id", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Group", "config_group", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("Environment", "environment", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("TenantId", "tenant_id", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("DisplayName", "display_name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("IsSensitive", "is_sensitive", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedBy", "updated_by", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_ConfigItem_Key_Environment_Tenant", new[] { "Key", "Environment", "TenantId" }, true),
            new IndexDefinition("IX_ConfigItem_Module", new[] { "ModuleId" }, false)
        });

    public static readonly IReadOnlyList<ModelDefinition> All = new[]
    {
        ConfigItem
    };
}
