using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public static class AuthorizationModelDefinitions
{
    public static readonly ModelDefinition Role = new(
        "Role",
        "auth_roles",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("IsEnabled", "is_enabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("IsBuiltIn", "is_built_in", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("CreatedBy", "created_by", FieldKind.Audit, FieldValueType.User, "string", false, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true),
            new FieldDefinition("UpdatedBy", "updated_by", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Role_Code", new[] { "Code" }, true)
        });

    public static readonly ModelDefinition Permission = new(
        "Permission",
        "auth_permissions",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("DisplayName", "display_name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("ModuleId", "module_id", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Group", "permission_group", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Permission_Code", new[] { "Code" }, true),
            new IndexDefinition("IX_Permission_Module", new[] { "ModuleId" }, false)
        });

    public static readonly ModelDefinition RolePermission = new(
        "RolePermission",
        "auth_role_permissions",
        ModelKind.Link,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("RoleId", "role_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("PermissionId", "permission_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("GrantedAt", "granted_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("GrantedBy", "granted_by", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_RolePermission_Role_Permission", new[] { "RoleId", "PermissionId" }, true)
        });

    public static readonly ModelDefinition UserRole = new(
        "UserRole",
        "auth_user_roles",
        ModelKind.Link,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("UserId", "user_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("RoleId", "role_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("AssignedAt", "assigned_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("AssignedBy", "assigned_by", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_UserRole_User_Role", new[] { "UserId", "RoleId" }, true),
            new IndexDefinition("IX_UserRole_Role", new[] { "RoleId" }, false)
        });

    public static readonly IReadOnlyList<ModelDefinition> All = new[]
    {
        Role,
        Permission,
        RolePermission,
        UserRole
    };
}
