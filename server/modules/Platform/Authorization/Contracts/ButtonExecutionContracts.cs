namespace ICP.Platform.Authorization.Contracts;

/// <summary>业务可按自己选择的时点检查规范对象；不要求按钮或执行合同，也不缓存允许结果。</summary>
public interface IObjectActionAccess
{
    /// <summary>对真实对象逐个检查全部动作，保留不可读与动作拒绝的区别；依赖故障原样传播。</summary>
    Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default);
    /// <summary>以正式业务读取上下文重新核验完整访问约束，其余规则与无上下文检查相同。</summary>
    Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        object readContext, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("DATA_SCOPE_READ_CONTEXT_UNSUPPORTED");
}

/// <summary>服务端固定的本次按钮调用来源，目标来自正式业务输入。</summary>
public sealed record ButtonExecutionContext(string ButtonId, string FeaturePosition, string BusinessBinding,
    string ResourceKey, string ObjectName, string ObjectId);

/// <summary>原保护时点取得的规范对象，不接受独立权限专用目标。</summary>
public sealed record ButtonExecutionTarget(string ResourceKey, string ObjectName, string ObjectId,
    IReadOnlyDictionary<string, object?> Before)
{
    /// <summary>来自同一正式输入的读取上下文；资源约束必须重新验证，不包含允许结果。</summary>
    public object? ReadContext { get; init; }
}

/// <summary>由真实路由适配登记的业务与正式目标类型，不包含权限要求。</summary>
public sealed record ButtonExecutionEndpoint(string BusinessBinding, string ResourceKey, string ObjectName);

/// <summary>平台从真实业务处理器与生效按钮取得的调用身份，不包含允许结果或客户端目标。</summary>
public sealed record ButtonBusinessInvocation(string ButtonId, string FeaturePosition, string BusinessBinding,
    string ActionKey, Type HandlerType);

/// <summary>业务提供原有保护边界与正式目标；平台独占目标检查及实际执行的衔接。</summary>
public interface IButtonBusinessHandler<TInput, TState, TResult>
{
    /// <summary>规范化正式输入并在业务所需保护范围内交出同一状态；不得提前执行业务变更。</summary>
    Task<TResult> WithinBoundaryAsync(TInput input, string actionKey,
        Func<TState, CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken);
    /// <summary>从已规范化状态取得实际目标；只有没有对象要求的按钮才允许返回空。</summary>
    ButtonExecutionTarget? Target(TState state);
    /// <summary>平台完成当前按钮要求后执行业务，关联对象检查仍由业务选择。</summary>
    Task<TResult> ExecuteAsync(TState state, CancellationToken cancellationToken);
}

/// <summary>真实处理器的公共调用入口，不规定业务事务长度，也不要求重复权限合同。</summary>
public interface IButtonBusinessExecutor
{
    /// <summary>在处理器的原保护边界内自动检查同一规范目标，再调用实际业务。</summary>
    Task<TResult> ExecuteAsync<TInput, TState, TResult>(IButtonBusinessHandler<TInput, TState, TResult> handler,
        TInput input, CancellationToken cancellationToken = default);
}

/// <summary>统一按钮执行管线；HTTP 身份不能成为内部允许凭证。</summary>
public interface IButtonExecutionAccess
{
    /// <summary>从实际 HTTP 入口和生效按钮绑定本次正式目标；缺失、串用或混写身份均拒绝。</summary>
    ButtonExecutionContext Bind(string resourceKey, string objectName, string operation, string objectId);
    /// <summary>从真实端点处理器绑定按钮身份；缺身份不能进入业务准备范围。</summary>
    ButtonBusinessInvocation BindHandler(Type handlerType);
    /// <summary>在处理器交出的原保护范围核对规范目标及按钮要求；没有要求不添加动作下限。</summary>
    Task<TResult> ExecuteAsync<TResult>(ButtonBusinessInvocation invocation, ButtonExecutionTarget? target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default);
    /// <summary>在真实对象已受保护后重新核身份与全部动作，全部允许才调用业务；异常不执行业务。</summary>
    Task<TResult> ExecuteAsync<TResult>(ButtonExecutionContext context, ButtonExecutionTarget target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default);
}
