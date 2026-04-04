namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 配置声明契约
/// </summary>
public interface IConfigProvider
{
    /// <summary>
    /// 获取模块配置架构
    /// </summary>
    IReadOnlyList<ConfigDescriptor> GetConfigSchema();
}

/// <summary>
/// 配置描述符
/// </summary>
public record ConfigDescriptor
{
    /// <summary>
    /// 配置键（唯一标识）
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// 配置显示名称
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 配置描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 配置类型（String, Int, Bool, Json 等）
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 默认值
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; init; }

    /// <summary>
    /// 配置分组（可选）
    /// </summary>
    public string? Group { get; init; }
}
