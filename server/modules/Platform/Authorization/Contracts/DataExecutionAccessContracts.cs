namespace ICP.Platform.Authorization.Contracts;

/// <summary>执行端允许的正式绑定及当前阶段，来自服务端注册而非客户端角色。</summary>
public sealed record DataExecutionBindingReference(string Execution, string Provider, string Stage);

/// <summary>有序要求执行保留不可读与非读动作拒绝的区别。</summary>
public enum DataExecutionDecision { Allowed, NotFound, Denied }

/// <summary>消费正式装配的有效要求；真实身份和候选由执行 owner 解析后传入。</summary>
public interface IDataExecutionAccess
{
    /// <summary>核验入口绑定与具体执行端；查询权限过滤继续由原SQL读取计划负责。</summary>
    void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey);
    /// <summary>正式阶段按当前事实执行单对象角色要求，不复用点击预检允许结果。</summary>
    Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, string objectId, CancellationToken cancellationToken = default);
    /// <summary>对服务端归一化后的真实候选执行候选角色要求，不读取不存在的对象。</summary>
    Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken = default);
}
