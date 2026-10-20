using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>每个请求的异步执行链独立保存当前查询来源；嵌套退出恢复外层来源。</summary>
public sealed class ButtonQueryReadScope : IButtonQueryReadScope
{
    private readonly AsyncLocal<ButtonQueryReadSource?> current = new();
    public ButtonQueryReadSource? Current => current.Value;

    /// <inheritdoc/>
    public IDisposable Enter(ButtonBusinessInvocation invocation, ButtonQueryTarget target)
    {
        var previous = current.Value;
        current.Value = new(invocation, target);
        return new Restore(() => current.Value = previous);
    }

    // 只恢复本异步调用链的来源；重复Dispose没有副作用。
    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? action = restore;
        /// <inheritdoc/>
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
