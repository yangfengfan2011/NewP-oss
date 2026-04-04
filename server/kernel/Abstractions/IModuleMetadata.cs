namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 模块元数据
/// </summary>
public interface IModuleMetadata
{
    /// <summary>
    /// 模块 ID
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// 显示名称
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 描述
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// 依赖的模块 ID 列表
    /// </summary>
    IReadOnlyList<string> Dependencies { get; }
}
