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
        // 每个缓存只观察一次数量；null 同时表达不可用，避免分别读取状态和数量形成混合诊断。
        var quantities = caches.Select(cache => (cache.Name, Count: cache.RecordCount)).OrderBy(cache => cache.Name, StringComparer.Ordinal).ToArray();
        var diagnostic = string.Join(", ", quantities.Select(cache => $"{cache.Name}: {cache.Count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}"));
        return ValueTask.FromResult(quantities.All(cache => cache.Count.HasValue)
            ? ReadinessContribution.Ready($"Named caches ready. Record counts: {diagnostic}.")
            : new ReadinessContribution(ReadinessState.Starting, "CACHE_UNAVAILABLE", $"Caches awaiting recovery. Record counts: {diagnostic}."));
    }
}
