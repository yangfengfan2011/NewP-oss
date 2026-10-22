using ICP.Platform.Kernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Sdk;

/// <summary>
/// 模块基类
/// </summary>
public abstract class ModuleBase : IModule, IModuleMetadata
{
    public abstract string ModuleId { get; }
    public abstract string DisplayName { get; }
    public virtual string Version => "1.0.0";
    public virtual string? Description => null;
    public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // 默认实现为空，子类可以重写
    }
}
