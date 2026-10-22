namespace ICP.Platform.Modules.Authorization.Models;

/// <summary>
/// 用户角色关系
/// </summary>
public class UserRole
{
    /// <summary>
    /// 关系 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 用户 ID
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// 角色 ID
    /// </summary>
    public required string RoleId { get; set; }

    /// <summary>
    /// 分配时间
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 分配人 ID
    /// </summary>
    public string? AssignedBy { get; set; }
}
