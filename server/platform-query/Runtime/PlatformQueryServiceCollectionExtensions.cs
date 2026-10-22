using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Query.Runtime;

public static class PlatformQueryServiceCollectionExtensions
{
    /// <summary>
    /// 注册平台查询内核默认执行器。
    /// 目前只提供 EF shared-type entity 实现，后续扩展仍应保持同一入口契约。
    /// </summary>
    public static IServiceCollection AddPlatformQueryKernel(this IServiceCollection services)
    {
        services.AddScoped<IPlatformQueryExecutor, EfPlatformQueryExecutor>();
        return services;
    }
}
