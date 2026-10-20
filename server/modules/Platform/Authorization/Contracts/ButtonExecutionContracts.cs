namespace ICP.Platform.Authorization.Contracts;

/// <summary>业务可按自己选择的时点检查规范对象；不要求按钮或执行合同，也不缓存允许结果。</summary>
public interface IObjectActionAccess
{
    /// <summary>对真实对象逐个检查全部动作，保留不可读与动作拒绝的区别；依赖故障原样传播。</summary>
    Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default);
}

/// <summary>服务端固定的本次按钮调用来源，目标来自正式业务输入。</summary>
public sealed record ButtonExecutionContext(string ButtonId, string FeaturePosition, string BusinessBinding,
    string ResourceKey, string ObjectName, string ObjectId);

/// <summary>原保护时点取得的规范对象，不接受独立权限专用目标。</summary>
public sealed record ButtonExecutionTarget(string ResourceKey, string ObjectName, string ObjectId,
    IReadOnlyDictionary<string, object?> Before);

/// <summary>由真实路由适配登记的业务与正式目标类型，不包含权限要求。</summary>
public sealed record ButtonExecutionEndpoint(string BusinessBinding, string ResourceKey, string ObjectName);

/// <summary>统一按钮执行管线；HTTP 身份不能成为内部允许凭证。</summary>
public interface IButtonExecutionAccess
{
    /// <summary>从实际 HTTP 入口和生效按钮绑定本次正式目标；缺失、串用或混写身份均拒绝。</summary>
    ButtonExecutionContext Bind(string resourceKey, string objectName, string operation, string objectId);
    /// <summary>在真实对象已受保护后重新核身份与全部动作，全部允许才调用业务；异常不执行业务。</summary>
    Task<TResult> ExecuteAsync<TResult>(ButtonExecutionContext context, ButtonExecutionTarget target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default);
}
