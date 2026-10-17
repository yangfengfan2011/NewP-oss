using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Authorization;

/// <summary>仅持有异步操作的观察时刻；新读取范围结束后恢复调用者的观察，不保存裁决结果。</summary>
public sealed class DataScopeEvaluationScope(TimeProvider clock) : IDataScopeEvaluationScope
{
    private readonly AsyncLocal<DateTimeOffset?> current = new();
    public DateTimeOffset ObservedAt => current.Value
        ?? throw new InvalidOperationException("DATA_SCOPE_OPERATION_REQUIRED");

    /// <inheritdoc/>
    public IDisposable BeginOperation() => Begin(false);

    /// <inheritdoc/>
    public IDisposable BeginReadAfterWrite() => Begin(true);

    // 普通嵌套复用，已提交后的读取替换观察；租约由调用者同步释放，异步子流独立持有上下文。
    private IDisposable Begin(bool fresh)
    {
        var previous = current.Value;
        current.Value = fresh || previous is null ? clock.GetUtcNow().ToUniversalTime() : previous;
        return new Lease(() => current.Value = previous);
    }

    private sealed class Lease(Action restore) : IDisposable
    {
        private Action? release = restore;
        /// <inheritdoc/>
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
