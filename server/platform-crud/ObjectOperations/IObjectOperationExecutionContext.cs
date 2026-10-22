using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 暴露当前统一对象行为核心的只读身份，供已迁移的旧 adapter 识别核心回调而不形成递归入口。
/// </summary>
public interface IObjectOperationExecutionContext
{
    /// <summary>判断当前 operation 是否正在执行精确对象、动作与目标的 behavior core。</summary>
    bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId);
}

/// <summary>分别跟踪当前 operation 与 behavior core；不检测或限制嵌套、递归及跨对象组合。</summary>
internal sealed class ObjectOperationExecutionContext : IObjectOperationExecutionContext
{
    private readonly AsyncLocal<OperationFrame?> _operation = new();
    private readonly AsyncLocal<OperationFrame?> _core = new();
    private readonly AsyncLocal<ExecutionState?> _state = new();

    // 当前异步调用链是否已经由一个根对象操作持有完整协调计划。
    internal bool IsActive => _state.Value is not null;

    // 根入口一次冻结闭包内每个源操作的直接目标；嵌套调用共享该状态并只能记录首个失败。
    internal IDisposable BeginRoot(
        IReadOnlyDictionary<ObjectOperationIdentity, IReadOnlySet<ObjectOperationIdentity>> callsByOperation)
    {
        ArgumentNullException.ThrowIfNull(callsByOperation);
        if (_state.Value is not null)
        {
            throw new InvalidOperationException("Object operation root scope is already active.");
        }
        var state = new ExecutionState(callsByOperation);
        _state.Value = state;
        return new RootScope(_state, state);
    }

    // 运行期调用必须由当前源操作直接声明；违反声明同时污染根事务。
    internal void EnsureTargetAllowed(ObjectOperationIdentity operation)
    {
        var state = _state.Value
            ?? throw new InvalidOperationException("Object operation root scope is unavailable.");
        var frame = _operation.Value;
        if (frame is not null
            && state.CallsByOperation.TryGetValue(new(frame.ObjectKey, frame.Action), out var allowedTargets)
            && allowedTargets.Contains(operation))
        {
            return;
        }

        var exception = new InvalidOperationException(
            $"OBJECT_OPERATION_TARGET_NOT_DECLARED: {operation.ObjectKey}/{operation.Action.Value}");
        state.Fail(exception);
        throw exception;
    }

    // 嵌套稳定拒绝即使被调用方忽略，也由根入口在提交前重新抛出。
    internal void Fail(StandardCrudMutationResult result) =>
        (_state.Value ?? throw new InvalidOperationException("Object operation root scope is unavailable."))
            .Fail(result);

    // 嵌套异常即使被调用方捕获，也会保留为根操作的最终失败。
    internal void Fail(Exception exception) =>
        (_state.Value ?? throw new InvalidOperationException("Object operation root scope is unavailable."))
            .Fail(exception);

    // 返回首个嵌套稳定拒绝；异常由 ThrowFailureException 负责保持原始类型。
    internal StandardCrudMutationResult? RejectedResult => _state.Value?.RejectedResult;

    // 若嵌套异常曾被吞掉，在根 delegate 返回前重新抛出同一异常。
    internal void ThrowFailureException()
    {
        if (_state.Value?.FailureException is { } exception)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <inheritdoc />
    public bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId)
    {
        var operation = _operation.Value;
        var core = _core.Value;
        return operation is not null
            && ReferenceEquals(operation, core)
            && string.Equals(operation.ObjectKey, objectKey, StringComparison.Ordinal)
            && operation.Action == action
            && string.Equals(operation.TargetId, targetId, StringComparison.Ordinal);
    }

    // 整个 before/core/after 期间保留当前 operation，供嵌套目标声明按直接调用者校验。
    internal IDisposable EnterOperation(string objectKey, ObjectOperationAction action, string? targetId)
    {
        var previous = _operation.Value;
        _operation.Value = new OperationFrame(objectKey, action, targetId);
        return new FrameScope(_operation, previous);
    }

    // 只在对象拥有者 behavior core 内标记当前 operation；before/after 和其他 operation 均不能命中。
    internal IDisposable EnterCore()
    {
        var operation = _operation.Value
            ?? throw new InvalidOperationException("Object operation scope is unavailable.");
        var previous = _core.Value;
        _core.Value = operation;
        return new FrameScope(_core, previous);
    }

    // OperationFrame 的实例身份区分嵌套层级，相同对象、动作和目标也不会混淆阶段。
    private sealed record OperationFrame(string ObjectKey, ObjectOperationAction Action, string? TargetId);

    private sealed class ExecutionState(
        IReadOnlyDictionary<ObjectOperationIdentity, IReadOnlySet<ObjectOperationIdentity>> callsByOperation)
    {
        internal IReadOnlyDictionary<ObjectOperationIdentity, IReadOnlySet<ObjectOperationIdentity>> CallsByOperation { get; } = callsByOperation;
        internal StandardCrudMutationResult? RejectedResult { get; private set; }
        internal Exception? FailureException { get; private set; }

        // 首个失败决定根操作结果，避免后续清理异常覆盖真实失败。
        internal void Fail(StandardCrudMutationResult result)
        {
            if (RejectedResult is null && FailureException is null)
            {
                RejectedResult = result;
            }
        }

        // 首个异常决定根操作结果，业务稳定拒绝若更早发生则保持其协议形状。
        internal void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (RejectedResult is null && FailureException is null)
            {
                FailureException = exception;
            }
        }
    }

    private sealed class RootScope(
        AsyncLocal<ExecutionState?> current,
        ExecutionState state) : IDisposable
    {
        private bool _disposed;

        // 根作用域只清理本次状态；它不拥有数据库事务或对象 frame。
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            if (!ReferenceEquals(current.Value, state))
            {
                throw new InvalidOperationException("Object operation root scopes must be disposed in reverse order.");
            }
            current.Value = null;
            _disposed = true;
        }
    }

    private sealed class FrameScope : IDisposable
    {
        private readonly AsyncLocal<OperationFrame?> _current;
        private readonly OperationFrame? _previous;

        // 保存进入前 frame，确保异常和取消同样恢复外层调用链。
        internal FrameScope(AsyncLocal<OperationFrame?> current, OperationFrame? previous)
        {
            _current = current;
            _previous = previous;
        }

        // 恢复外层 frame；该类型不拥有事务和递归策略。
        public void Dispose() => _current.Value = _previous;
    }
}
