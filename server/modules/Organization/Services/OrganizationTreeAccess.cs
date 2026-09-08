using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>一个终端读取策略组合既有公司与部门权限，不新增权限码。</summary>
public static class OrganizationTreeAccess
{
    public const string PolicyName = "Organization.Tree.Read";

    /// <summary>复用 PermissionGate 的两项要求，保持缺少任一权限时拒绝读取。</summary>
    public static void Configure(AuthorizationOptions options) => options.AddPolicy(PolicyName,
        policy => policy.RequireAuthenticatedUser().AddRequirements(
            new PermissionAuthorizationRequirement(OrganizationPermissions.CompanyView),
            new PermissionAuthorizationRequirement(OrganizationPermissions.DepartmentView)));
}
