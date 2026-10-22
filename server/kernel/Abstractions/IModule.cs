using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Kernel.Abstractions;

/// <summary>
/// 模块契约接口
/// </summary>
public interface IModule
{
    /// <summary>
    /// 模块唯一标识
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// 模块显示名称
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 模块版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 配置服务
    /// </summary>
    void ConfigureServices(IServiceCollection services);
}
