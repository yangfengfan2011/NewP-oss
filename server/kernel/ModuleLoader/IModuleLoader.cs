using ICP.Platform.Kernel.Abstractions;

namespace ICP.Platform.Kernel.ModuleLoader;

/// <summary>
/// 模块加载器接口
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    /// 发现所有可用模块
    /// </summary>
    IReadOnlyList<IModule> DiscoverModules();

    /// <summary>
    /// 按依赖顺序排序模块
    /// </summary>
    IReadOnlyList<IModule> SortByDependencies(IReadOnlyList<IModule> modules);
}
