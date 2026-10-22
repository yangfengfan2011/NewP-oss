namespace ICP.Platform.Modules.Authorization.Models;

/// <summary>
/// 角色实体
/// </summary>
public class Role
{
    /// <summary>
    /// 角色 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 角色代码（唯一标识）
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// 角色名称
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 角色描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否系统内置角色（不可删除）
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建人 ID
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 更新人 ID
    /// </summary>
    public string? UpdatedBy { get; set; }
}
