namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 权限声明契约
/// </summary>
public interface IPermissionProvider
{
    /// <summary>
    /// 获取模块声明的权限点
    /// </summary>
    IReadOnlyList<PermissionDescriptor> GetPermissions();
}

/// <summary>
/// 权限描述符
/// </summary>
public record PermissionDescriptor
{
    /// <summary>
    /// 权限代码（唯一标识）
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 权限显示名称
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 权限描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; init; }

    /// <summary>
    /// 权限分组（可选）
    /// </summary>
    public string? Group { get; init; }
}
