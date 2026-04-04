namespace ICP.Platform.Modules.Authorization.Models;

/// <summary>
/// 角色权限关系
/// </summary>
public class RolePermission
{
    /// <summary>
    /// 关系 ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 角色 ID
    /// </summary>
    public required string RoleId { get; set; }

    /// <summary>
    /// 权限 ID
    /// </summary>
    public required string PermissionId { get; set; }

    /// <summary>
    /// 授予时间
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 授予人 ID
    /// </summary>
    public string? GrantedBy { get; set; }
}
