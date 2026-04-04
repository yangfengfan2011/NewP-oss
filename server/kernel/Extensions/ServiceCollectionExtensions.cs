using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModuleLoader;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Kernel.Extensions;

/// <summary>
/// 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加模块系统
    /// </summary>
    public static IServiceCollection AddModuleSystem(
        this IServiceCollection services,
        Action<ModuleOptions>? configure = null)
    {
        var options = new ModuleOptions();
        configure?.Invoke(options);

        // 注册模块注册表为单例
        var registry = new ModuleRegistry();
        services.AddSingleton(registry);

        // 创建模块加载器
        var loader = new AssemblyModuleLoader(options.Assemblies);

        // 发现并排序模块
        var modules = loader.DiscoverModules();
        var sortedModules = loader.SortByDependencies(modules);

        // 注册并配置每个模块
        foreach (var module in sortedModules)
        {
            registry.RegisterModule(module);
            module.ConfigureServices(services);
        }

        return services;
    }
}

/// <summary>
/// 模块配置选项
/// </summary>
public class ModuleOptions
{
    /// <summary>
    /// 要扫描的程序集列表
    /// </summary>
    public List<System.Reflection.Assembly> Assemblies { get; } = new();
}
