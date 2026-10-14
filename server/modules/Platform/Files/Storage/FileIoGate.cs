namespace ICP.Platform.Modules.Files.Storage;

/// <summary>单 Host 进程级每键 gate；所有 writer 句柄、rename 与回收删除共用它。</summary>
public sealed class FileIoGate
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>意图提交前登记活跃 writer，覆盖提交到取得 I/O gate 之间的恢复竞赛。</summary>
    public WriterReservation ReserveWriter(string key, CancellationToken token)
    {
        lock (_sync)
        {
            var entry = Retain(key);
            if (entry.Writer is not null) { Release(key, entry); throw new InvalidOperationException("Files.WriterAlreadyActive"); }
            entry.Writer = CancellationTokenSource.CreateLinkedTokenSource(token);
            return new(this, key, entry, entry.Writer);
        }
    }

    /// <summary>不持数据库事务等待此 gate；返回 scope 包住全部 I/O 句柄生命周期。</summary>
    public async ValueTask<IDisposable> EnterAsync(string key, CancellationToken token)
    {
        Entry entry;
        lock (_sync) entry = Retain(key);
        try { await entry.Gate.WaitAsync(token); }
        catch { lock (_sync) Release(key, entry); throw; }
        return new GateScope(this, key, entry);
    }

    /// <summary>仅用于判断无活 writer 的 receiving 恢复，不把内存当持久状态。</summary>
    public bool HasWriter(string key)
    {
        lock (_sync) return _entries.TryGetValue(key, out var entry) && entry.Writer is not null;
    }

    /// <summary>短数据库事务退出后取消该 writer；回收仍等待句柄退出的同键 gate。</summary>
    public void Abort(string key)
    {
        lock (_sync)
            if (_entries.TryGetValue(key, out var entry)) entry.Writer?.Cancel();
    }

    // 引用覆盖等待者和 writer reservation，最后释放时移除键以避免无限积累。
    private Entry Retain(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries.Add(key, entry = new());
        entry.References++;
        return entry;
    }

    // 调用方必须持有 _sync；没有等待者时可安全移除和释放 semaphore。
    private void Release(string key, Entry entry)
    {
        if (--entry.References != 0) return;
        _entries.Remove(key);
        entry.Gate.Dispose();
    }

    public sealed class WriterReservation : IDisposable
    {
        private FileIoGate? _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private readonly CancellationTokenSource _source;
        public CancellationToken Token => _source.Token;

        // Reservation 的构造只由父 gate 在锁内完成。
        internal WriterReservation(FileIoGate owner, string key, Entry entry, CancellationTokenSource source)
        { _owner = owner; _key = key; _entry = entry; _source = source; }

        /// <inheritdoc/>
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._sync)
            {
                _entry.Writer = null;
                _source.Dispose();
                owner.Release(_key, _entry);
            }
        }
    }

    internal sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CancellationTokenSource? Writer { get; set; }
        public int References { get; set; }
    }

    private sealed class GateScope(FileIoGate owner, string key, Entry entry) : IDisposable
    {
        private FileIoGate? _owner = owner;
        /// <inheritdoc/>
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            entry.Gate.Release();
            lock (current._sync) current.Release(key, entry);
        }
    }
}
