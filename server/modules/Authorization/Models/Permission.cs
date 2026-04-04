namespace ICP.Platform.Modules.Authorization.Models;

/// <summary>
/// 权限实体
/// </summary>
public class Permission
{
    /// <summary>
    /// 权限 ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 权限代码（唯一标识）
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// 权限显示名称
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// 权限描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; set; }

    /// <summary>
    /// 权限分组
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
