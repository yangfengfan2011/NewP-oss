namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 暴露当前统一对象行为的只读身份，供已迁移的旧 adapter 识别核心回调而不形成递归入口。
/// </summary>
public interface IObjectOperationExecutionContext
{
    /// <summary>判断当前异步调用链是否正在执行精确的对象、动作与目标。</summary>
    bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId);
}

/// <summary>仅跟踪当前调用链；不检测或限制嵌套、递归及跨对象组合。</summary>
internal sealed class ObjectOperationExecutionContext : IObjectOperationExecutionContext
{
    private readonly AsyncLocal<Frame?> _current = new();

    /// <inheritdoc />
    public bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId)
    {
        var frame = _current.Value;
        return frame is not null
            && string.Equals(frame.ObjectKey, objectKey, StringComparison.Ordinal)
            && frame.Action == action
            && string.Equals(frame.TargetId, targetId, StringComparison.Ordinal);
    }

    // 进入一个行为核心并保留外层 frame，退出时只恢复调用链本身的先前状态。
    internal IDisposable Enter(string objectKey, ObjectOperationAction action, string? targetId)
    {
        var previous = _current.Value;
        _current.Value = new Frame(objectKey, action, targetId);
        return new Scope(_current, previous);
    }

    // Frame 只保存当前核心身份，不参与任何递归或嵌套裁决。
    private sealed record Frame(string ObjectKey, ObjectOperationAction Action, string? TargetId);

    private sealed class Scope : IDisposable
    {
        private readonly AsyncLocal<Frame?> _current;
        private readonly Frame? _previous;

        // 保存进入前 frame，确保异常和取消同样恢复外层调用链。
        internal Scope(AsyncLocal<Frame?> current, Frame? previous)
        {
            _current = current;
            _previous = previous;
        }

        // 恢复外层 frame；该类型不拥有事务和递归策略。
        public void Dispose() => _current.Value = _previous;
    }
}
