namespace ICP.Platform.Modules.Authorization;

/// <summary>
/// Authorization 模块拥有的稳定权限代码。
/// </summary>
public static class AuthorizationPermissions
{
    public const string RoleView = "Authorization.Role.View";
    public const string RoleCreate = "Authorization.Role.Create";
    public const string RoleEdit = "Authorization.Role.Edit";
    public const string RoleDelete = "Authorization.Role.Delete";
    public const string PermissionView = "Authorization.Permission.View";
    public const string PermissionAssign = "Authorization.Permission.Assign";
    public const string UserAssignRole = "Authorization.User.AssignRole";
}
