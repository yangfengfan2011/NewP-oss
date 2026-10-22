namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 菜单声明契约
/// </summary>
public interface IMenuProvider
{
    /// <summary>
    /// 获取模块声明的菜单项
    /// </summary>
    IReadOnlyList<MenuDescriptor> GetMenus();
}

/// <summary>
/// 菜单描述符
/// </summary>
public record MenuDescriptor
{
    /// <summary>
    /// 菜单 ID（唯一标识）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 菜单显示名称
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 父菜单 ID（null 表示顶级菜单）
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// 路由路径
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    /// 图标
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// 排序序号
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 所属模块 ID
    /// </summary>
    public required string ModuleId { get; init; }
}
