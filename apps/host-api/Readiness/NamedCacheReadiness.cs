using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.HostApi.Readiness;

/// <summary>只观察已登记缓存，恢复中的未就绪不会被启动监督器误判为终止性启动失败。</summary>
public sealed class NamedCacheReadinessContributor(IEnumerable<IPlatformCacheStatus> caches) : IReadinessContributor
{
    public string Name => "named-caches";
    public int Order => 200;
    public bool IsRequired => true;

    /// <inheritdoc/>
    public ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unavailable = caches.Where(cache => !cache.IsAvailable).Select(cache => cache.Name).Order(StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(unavailable.Length == 0
            ? ReadinessContribution.Ready("Named caches ready.")
            : new ReadinessContribution(ReadinessState.Starting, "CACHE_UNAVAILABLE", $"Caches awaiting recovery: {string.Join(", ", unavailable)}."));
    }
}
