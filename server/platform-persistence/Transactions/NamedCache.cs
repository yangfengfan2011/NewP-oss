namespace ICP.Platform.Persistence.Transactions;

/// <summary>具名缓存统一持有写协调、只读结果和提交发布窗口，具体缓存拥有数据规则。</summary>
public abstract class NamedCache<TSnapshot> : IPlatformCommitParticipant where TSnapshot : class
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly object _state = new();
    private TSnapshot? _snapshot;
    private TaskCompletionSource? _publication;
    public abstract string Name { get; }

    /// <summary>具体缓存从一致的数据集合装载完整结果。</summary>
    protected abstract ValueTask<TSnapshot> LoadAsync(CancellationToken cancellationToken);
    /// <summary>从实际变更局部构造新结果，禁止修改已发布结果。</summary>
    protected abstract ValueTask<TSnapshot> ApplyAsync(TSnapshot snapshot, IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken);
    /// <summary>单一发布入口；可在测试中注入发布故障，生产实现只做原子替换。</summary>
    protected virtual void Publish(TSnapshot snapshot) { lock (_state) _snapshot = snapshot; }

    /// <summary>初始化只装载一次；失败保持不可用且允许显式重试。</summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken);
        try
        {
            lock (_state) { if (_snapshot is not null) return; }
            Publish(await LoadAsync(cancellationToken));
        }
        finally { _writer.Release(); }
    }

    /// <summary>读取同一完整结果；提交发布期间等待，故障时不放行旧数据。</summary>
    public async ValueTask<TSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task? publication;
            lock (_state)
            {
                publication = _publication?.Task;
                if (publication is null) return _snapshot ?? throw new PlatformCacheUnavailableException(Name);
            }
            await publication.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken);
        lock (_state)
        {
            if (_snapshot is not null) return new CommitScope(this, _snapshot);
        }
        _writer.Release();
        throw new PlatformCacheUnavailableException(Name);
    }

    private sealed class CommitScope(NamedCache<TSnapshot> owner, TSnapshot snapshot) : IPlatformCommitScope
    {
        private TSnapshot _candidate = snapshot;
        private bool _disposed;
        /// <inheritdoc/>
        public async ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken) =>
            _candidate = await owner.ApplyAsync(snapshot, changes, cancellationToken);
        /// <inheritdoc/>
        public void BlockReads() { lock (owner._state) owner._publication = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        /// <inheritdoc/>
        public ValueTask PublishAsync() { owner.Publish(_candidate); return ValueTask.CompletedTask; }
        /// <inheritdoc/>
        public void MarkUnavailable() { lock (owner._state) owner._snapshot = null; }
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            lock (owner._state)
            {
                owner._publication?.TrySetResult();
                owner._publication = null;
            }
            owner._writer.Release();
            return ValueTask.CompletedTask;
        }
    }
}
