using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 为 Controller 或 action 声明显式业务权限要求。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    internal const string PolicyPrefix = "Permission:";

    public RequirePermissionAttribute(string permissionCode)
    {
        var requirement = new PermissionRequirement(permissionCode);
        PermissionCode = requirement.PermissionCode;
        Policy = PolicyPrefix + PermissionCode;
    }

    public string PermissionCode { get; }
}
