using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Readiness;

/// <summary>
/// 注册 Host readiness 聚合接口。
/// </summary>
public static class ReadinessServiceCollectionExtensions
{
    /// <summary>注册聚合器与通用缓存状态观察；模块仅登记缓存，不依赖 Host 类型。</summary>
    public static IServiceCollection AddHostReadiness(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IReadinessEvaluator, ReadinessEvaluator>();
        services.AddScoped<IReadinessContributor, NamedCacheReadinessContributor>();
        return services;
    }
}
