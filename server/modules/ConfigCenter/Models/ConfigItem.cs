namespace ICP.Platform.Modules.ConfigCenter.Models;

/// <summary>
/// 配置项实体
/// </summary>
public class ConfigItem
{
    /// <summary>
    /// 配置项 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 配置键（唯一标识）
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// 配置值
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// 配置类型（String, Int, Bool, Json 等）
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; set; }

    /// <summary>
    /// 配置分组
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// 环境标识（null 表示全局，否则为特定环境）
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// 租户标识（null 表示全局，否则为特定租户）
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// 配置描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否敏感配置（如密码、密钥等）
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新人
    /// </summary>
    public string? UpdatedBy { get; set; }
}
