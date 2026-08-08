using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// ASP.NET Core authorization pipeline 中的显式业务权限要求。
/// </summary>
public sealed class PermissionAuthorizationRequirement : IAuthorizationRequirement
{
    public PermissionAuthorizationRequirement(string permissionCode)
    {
        Requirement = new PermissionRequirement(permissionCode);
    }

    public PermissionRequirement Requirement { get; }
}
