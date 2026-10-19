namespace ICP.Platform.Authorization.Contracts;

/// <summary>执行端允许的正式绑定及当前阶段，来自服务端注册而非客户端角色。</summary>
public sealed record DataExecutionBindingReference(string Execution, string Provider, string Stage)
{
    public string? Branch { get; init; }
}

/// <summary>执行owner的真实解析输入；Source仅供已登记服务端解析器，不能作为授权结果。</summary>
public sealed record DataExecutionTargetInput(string ResourceKey, string? ObjectId, object? Source = null);

/// <summary>可信解析器返回规范对象身份和业务事实；事实随当前阶段重新取得，不缓存允许结果。</summary>
public sealed record DataExecutionResolvedTarget(string ResourceKey, string ObjectId, object? Facts = null);

/// <summary>逐对象裁决保留实际身份，业务owner可在继续下一对象前消费原业务约束。</summary>
public sealed record DataExecutionTargetDecision(DataExecutionDecision Decision, DataExecutionResolvedTarget? Target);

/// <summary>owner登记的完整读取解析器；执行合同选择Key，客户端不能选择或跳过。</summary>
public interface IDataExecutionTargetResolver
{
    string Key { get; }
    string ModuleId { get; }
    Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token);
}

/// <summary>有序要求执行保留不可读与非读动作拒绝的区别。</summary>
public enum DataExecutionDecision { Allowed, NotFound, Denied }

/// <summary>消费正式装配的有效要求；真实身份和候选由执行 owner 解析后传入。</summary>
public interface IDataExecutionAccess
{
    /// <summary>核验入口绑定与具体执行端；查询权限过滤继续由原SQL读取计划负责。</summary>
    void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey);
    /// <summary>通用端点由真实metadata及资源owner选择受控绑定；仅明确登记的旧消费者返回null。</summary>
    DataExecutionBindingReference? BindResourceEndpoint(string resourceKey) => throw new NotSupportedException();
    /// <summary>完整角色先核基数，再按输入顺序裁决；每次枚举均使用当前阶段事实。</summary>
    IAsyncEnumerable<DataExecutionTargetDecision> EvaluateObjectsAsync(DataExecutionBindingReference binding, string role,
        IReadOnlyList<DataExecutionTargetInput> targets, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    /// <summary>正式阶段按当前事实执行单对象角色要求，不复用点击预检允许结果。</summary>
    Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, string objectId, CancellationToken cancellationToken = default);
    /// <summary>对服务端归一化后的真实候选执行候选角色要求，不读取不存在的对象。</summary>
    Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken = default);
}
