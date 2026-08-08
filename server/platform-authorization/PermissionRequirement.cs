namespace ICP.Platform.Authorization;

/// <summary>
/// 表示 endpoint 显式声明的单一业务权限要求。
/// </summary>
public sealed record PermissionRequirement
{
    public PermissionRequirement(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        PermissionCode = permissionCode;
    }

    public string PermissionCode { get; }
}
