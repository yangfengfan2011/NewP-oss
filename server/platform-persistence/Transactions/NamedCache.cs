using System.Diagnostics;

namespace ICP.Platform.Persistence.Transactions;

/// <summary>具名缓存统一持有写协调、只读结果和提交发布窗口，具体缓存拥有数据规则。</summary>
public abstract class NamedCache<TSnapshot> : IPlatformCommitParticipant, IPlatformCacheStatus where TSnapshot : class
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly object _state = new();
    private TSnapshot? _snapshot;
    private long? _recordCount;
    private TSnapshot? _repairBase;
    private IReadOnlyList<PlatformRecordChange>? _repairChanges;
    private TaskCompletionSource? _publication;
    private TaskCompletionSource _unavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimeSpan? _lastMaintenanceDuration;
    public abstract string Name { get; }
    public bool IsAvailable { get { lock (_state) return _snapshot is not null; } }
    /// <inheritdoc/>
    public long? RecordCount { get { lock (_state) return _recordCount; } }
    /// <summary>最近一次实际装载或刷新尝试的耗时；不包含等待协调权的时间，也不表示性能达标。</summary>
    public TimeSpan? LastMaintenanceDuration { get { lock (_state) return _lastMaintenanceDuration; } }

    /// <summary>具体缓存从一致的数据集合装载完整结果。</summary>
    protected abstract ValueTask<TSnapshot> LoadAsync(CancellationToken cancellationToken);
    /// <summary>从实际变更局部构造新结果，禁止修改已发布结果。</summary>
    protected abstract ValueTask<TSnapshot> ApplyAsync(TSnapshot snapshot, IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken);
    /// <summary>只读取已确定受影响记录的当前持久化事实；不重放原业务写入。</summary>
    protected abstract ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken);
    /// <summary>具体缓存以已有集合数量统计完整记录，不遍历档案或重复计算关系索引。</summary>
    protected abstract long CountRecords(TSnapshot snapshot);
    /// <summary>单一发布入口；可在测试中注入发布故障，生产实现只做原子替换。</summary>
    protected virtual void Publish(TSnapshot snapshot)
    {
        var recordCount = CountRecords(snapshot);
        lock (_state)
        {
            _snapshot = snapshot;
            _recordCount = recordCount;
            // 健康写入沿用尚未触发的信号，不能把已等待的恢复服务留在旧任务上。
            if (_unavailable.Task.IsCompleted) _unavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>初次装载完整结果；故障后优先读取已知范围修复，成功请求合并为一次装载。</summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken);
        long? started = null;
        try
        {
            lock (_state) { if (_snapshot is not null) return; }
            started = Stopwatch.GetTimestamp();
            var candidate = await LoadOrRepairAsync(cancellationToken);
            Publish(candidate);
            _repairBase = null;
            _repairChanges = null;
        }
        finally { RecordDuration(started); _writer.Release(); }
    }

    /// <summary>
    /// 无关联写入的独立局部刷新：自行取得协调权，从数据库读取完整记录并校验发布。
    /// 事务内写入必须通过 RegisterChange 登记，不能在持有事务协调权时调用本方法。
    /// </summary>
    public async ValueTask RefreshAsync(IReadOnlyCollection<PlatformCacheRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return;
        var changes = records.Distinct().Select(record => new PlatformRecordChange(record.ModelKey, record.Id, null, null)).ToArray();
        await _writer.WaitAsync(cancellationToken);
        long? started = null;
        try
        {
            TSnapshot snapshot;
            lock (_state) snapshot = _snapshot ?? throw new PlatformCacheUnavailableException(Name);
            started = Stopwatch.GetTimestamp();
            lock (_state) _publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Publish(await ApplyAsync(snapshot, await ReloadAsync(changes, cancellationToken), cancellationToken));
            }
            catch
            {
                // 健康刷新期间读写等待；失败或取消后才封闭旧结果，保留准确范围供后台修复。
                SetUnavailable(snapshot, changes);
                throw;
            }
        }
        finally { RecordDuration(started); ReleasePublication(); _writer.Release(); }
    }

    /// <summary>启动及故障信号驱动恢复；健康期间不轮询数据库，失败后留出重试间隔，停止时释放所有等待。</summary>
    public async Task MaintainAsync(Action<Exception> reportFailure, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await InitializeAsync(cancellationToken);
                    Task unavailable;
                    lock (_state) unavailable = _snapshot is null ? Task.CompletedTask : _unavailable.Task;
                    await unavailable.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    reportFailure(exception);
                    // 仅为失败重试节流，不是数据轮询或性能通过阈值。
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    // 读取失败继续重试同一范围；只有局部候选无法成立，才升级为完整集合重建。
    private async ValueTask<TSnapshot> LoadOrRepairAsync(CancellationToken cancellationToken)
    {
        if (_repairBase is null || _repairChanges is null) return await LoadAsync(cancellationToken);
        var changes = await ReloadAsync(_repairChanges, cancellationToken);
        try { return await ApplyAsync(_repairBase, changes, cancellationToken); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return await LoadAsync(cancellationToken);
        }
    }

    // 只保留私有修复基底；读取和新业务写入均不能取得已知失效结果。
    private void SetUnavailable(TSnapshot snapshot, IReadOnlyList<PlatformRecordChange>? changes)
    {
        lock (_state)
        {
            _snapshot = null;
            _recordCount = null;
            _repairBase = snapshot;
            _repairChanges = changes;
            _unavailable.TrySetResult();
        }
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
        cancellationToken.ThrowIfCancellationRequested();
        // 故障恢复可能等待数据库；已知不可用的依赖写入应立即503，不能排队后意外执行。
        lock (_state) { if (_snapshot is null) throw new PlatformCacheUnavailableException(Name); }
        await _writer.WaitAsync(cancellationToken);
        lock (_state)
        {
            if (_snapshot is not null) return new CommitScope(this, _snapshot);
        }
        _writer.Release();
        throw new PlatformCacheUnavailableException(Name);
    }

    // 发布或失败状态已确定后统一唤醒读取者；读取者自行重新检查状态。
    private void ReleasePublication()
    {
        lock (_state)
        {
            _publication?.TrySetResult();
            _publication = null;
        }
    }

    // 只记录实际维护尝试；合并的空操作不能覆盖最近一次有效诊断。
    private void RecordDuration(long? started)
    {
        if (started is null) return;
        lock (_state) _lastMaintenanceDuration = Stopwatch.GetElapsedTime(started.Value);
    }

    private sealed class CommitScope(NamedCache<TSnapshot> owner, TSnapshot snapshot) : IPlatformCommitScope
    {
        private TSnapshot _candidate = snapshot;
        private IReadOnlyList<PlatformRecordChange>? _changes;
        private bool _disposed;
        /// <inheritdoc/>
        public async ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
        {
            _changes = changes.ToArray();
            _candidate = await owner.ApplyAsync(snapshot, changes, cancellationToken);
        }
        /// <inheritdoc/>
        public void BlockReads() { lock (owner._state) owner._publication = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        /// <inheritdoc/>
        public ValueTask PublishAsync() { owner.Publish(_candidate); return ValueTask.CompletedTask; }
        /// <inheritdoc/>
        public void MarkUnavailable()
        {
            owner.SetUnavailable(snapshot, _changes);
        }
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            owner.ReleasePublication();
            owner._writer.Release();
            return ValueTask.CompletedTask;
        }
    }
}
