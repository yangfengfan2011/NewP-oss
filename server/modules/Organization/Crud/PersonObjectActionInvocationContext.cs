using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 在正式服务入口与人员对象行为之间传递可信 actor；业务输入仍由对象操作 before 上下文单独管理。
/// </summary>
public sealed class PersonObjectActionInvocationContext
{
    private readonly AsyncLocal<Frame?> _current = new();

    /// <summary>为一次人员专用动作保存服务端 actor，并支持同一异步流中的嵌套动作。</summary>
    internal IDisposable Begin(object actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var frame = new Frame(actor, _current.Value);
        _current.Value = frame;
        return new Scope(this, frame);
    }

    /// <summary>返回旧服务入口保存的 actor；直接公共对象入口没有该桥接值。</summary>
    internal TActor? Current<TActor>() where TActor : class =>
        _current.Value?.Actor as TActor;

    private sealed record Frame(object Actor, Frame? Parent);

    private sealed class Scope(PersonObjectActionInvocationContext owner, Frame frame) : IDisposable
    {
        private bool _disposed;

        /// <summary>按进入顺序恢复外层 actor，避免嵌套动作污染后续调用。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            if (!ReferenceEquals(owner._current.Value, frame))
            {
                throw new InvalidOperationException("Person action actor scopes must be disposed in reverse order.");
            }
            owner._current.Value = frame.Parent;
            _disposed = true;
        }
    }
}
