using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Persistence.Transactions;

/// <summary>启动尝试完整装载，初载故障保留 Host 并交给信号驱动的自动恢复。</summary>
public sealed class NamedCacheInitializer<TCache, TSnapshot>(TCache cache, ILogger<NamedCacheInitializer<TCache, TSnapshot>> logger)
    : BackgroundService where TCache : NamedCache<TSnapshot> where TSnapshot : class
{
    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await cache.InitializeAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { ReportFailure(exception); }
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => cache.MaintainAsync(ReportFailure, stoppingToken);

    // 完整异常只进入宿主日志；公开 readiness 沿具名缓存就绪边界，不泄露数据库内容。
    private void ReportFailure(Exception exception) => logger.LogWarning(exception,
        "Cache {CacheName} is unavailable after {ElapsedMilliseconds} ms; recovery will retry.", cache.Name, cache.LastMaintenanceDuration?.TotalMilliseconds);
}

/// <summary>具名缓存的统一装配：同一模型清单登记提交参与，缓存实例、就绪状态与初始化器一并注册。</summary>
public static class NamedCacheServiceCollectionExtensions
{
    /// <summary>
    /// 注册单例缓存、<see cref="IPlatformCacheStatus"/>、逐模型 <see cref="PlatformCommitRegistration"/> 与初始化器。
    /// <paramref name="participant"/> 缺省时缓存本身参与提交；包装缓存提交 scope 的 owner 传入从当前 scope 解析的参与者。
    /// </summary>
    public static IServiceCollection AddNamedCache<TCache, TSnapshot>(this IServiceCollection services, ImmutableArray<string> models,
        Func<IServiceProvider, IPlatformCommitParticipant>? participant = null)
        where TCache : NamedCache<TSnapshot> where TSnapshot : class
    {
        services.AddSingleton<TCache>();
        services.AddSingleton<IPlatformCacheStatus>(provider => provider.GetRequiredService<TCache>());
        foreach (var model in models)
            services.AddScoped(provider => new PlatformCommitRegistration(model, participant?.Invoke(provider) ?? provider.GetRequiredService<TCache>()));
        services.AddHostedService<NamedCacheInitializer<TCache, TSnapshot>>();
        return services;
    }
}
