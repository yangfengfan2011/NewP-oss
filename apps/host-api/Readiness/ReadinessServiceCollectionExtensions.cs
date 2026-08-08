using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Readiness;

/// <summary>
/// 注册 Host readiness 聚合接口。
/// </summary>
public static class ReadinessServiceCollectionExtensions
{
    public static IServiceCollection AddHostReadiness(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReadinessEvaluator, ReadinessEvaluator>();
        return services;
    }
}
