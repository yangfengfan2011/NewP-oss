using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public static class AuthorizationModelDefinitions
{
    public static readonly ModelDefinition Role = new(
        "Role",
        "Role",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "Description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("IsBuiltIn", "IsBuiltIn", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true),
            new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Role_Code", new[] { "Code" }, true)
        });

    public static readonly ModelDefinition Permission = new(
        "Permission",
        "Permission",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("DisplayName", "DisplayName", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Description", "Description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("ModuleId", "ModuleId", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Group", "Group", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Permission_Code", new[] { "Code" }, true),
            new IndexDefinition("IX_Permission_Module", new[] { "ModuleId" }, false)
        });

    public static readonly ModelDefinition RolePermission = new(
        "RolePermission",
        "RolePermission",
        ModelKind.Link,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("RoleId", "RoleId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("PermissionId", "PermissionId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("GrantedAt", "GrantedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("GrantedBy", "GrantedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_RolePermission_Role_Permission", new[] { "RoleId", "PermissionId" }, true)
        });

    public static readonly ModelDefinition UserRole = new(
        "UserRole",
        "UserRole",
        ModelKind.Link,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("UserId", "UserId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("RoleId", "RoleId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("AssignedAt", "AssignedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("AssignedBy", "AssignedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
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
