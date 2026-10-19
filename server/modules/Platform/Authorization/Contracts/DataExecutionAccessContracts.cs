namespace ICP.Platform.Authorization.Contracts;

/// <summary>执行端允许的正式绑定及当前阶段，来自服务端注册而非客户端角色。</summary>
public sealed record DataExecutionBindingReference(string Execution, string Provider, string Stage)
{
    public string? Branch { get; init; }
}

/// <summary>服务端选定的查询来源；仅传执行绑定和角色，不代表允许结果或对象ID集合。</summary>
public sealed record DataExecutionQueryContext(DataExecutionBindingReference Binding, string Role)
{
    /// <summary>与查询定义分开的真实显式ID集合，由服务端按既有请求规则归一化。</summary>
    public IReadOnlyList<DataExecutionReadTargetSet> TargetSets { get; init; } = [];
}

/// <summary>查询伴随的当前值等集合角色；ID只定位目标，不是允许证明。</summary>
public sealed record DataExecutionReadTargetSet(string Role, IReadOnlyList<string> Ids);

/// <summary>内部调用者的类型来自真实服务装配，不接受HTTP头、JSON或允许结果。</summary>
public sealed record DataExecutionInternalReadBinding(string Execution, string Provider, Type Caller, string? Member = null)
{
    public string CallerIdentity => Caller.FullName + (Member is null ? "" : "." + Member);
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
public partial interface IDataExecutionAccess
{
    /// <summary>核验入口绑定与具体执行端；查询权限过滤继续由原SQL读取计划负责。</summary>
    void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey);
    /// <summary>按正式查询来源取得原数据库读取计划；调用方继续在Count/排序/分页之前组合它。</summary>
    Task<DataScopeReadPlan> GetQueryReadPlanAsync(DataExecutionQueryContext context, string resourceKey,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    /// <summary>单对象读取在原SQL位置核对真实入口允许的执行；保留编辑准备与直接查看的不同来源。</summary>
    Task<DataScopeReadPlan> GetObjectReadPlanAsync(IReadOnlyList<DataExecutionBindingReference> bindings,
        string resourceKey, string role, string objectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    /// <summary>固定内部来源在原批量读取位置核对角色、资源和调用者；不借用HTTP位置或逐对象预检。</summary>
    Task<IReadOnlySet<string>> GetInternalReadableIdsAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    /// <summary>内部调用者在原单对象SQL读取位置组合完整read计划；ID只核角色基数，不逐项预检。</summary>
    Task<DataScopeReadPlan> GetInternalReadPlanAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
    /// <summary>在候选补齐身份、归属和审计字段后消费本次写绑定，不要求新对象已可读。</summary>
    Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionWriteContext source, string resourceKey, string role,
        IReadOnlyDictionary<string, object?> candidate, CancellationToken token = default) => throw new NotSupportedException();

    /// <summary>在原旧值保护位置核对单次写来源与实际目标，再按有效要求执行现有对象裁决。</summary>
    Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionWriteContext source, string role,
        DataExecutionWriteFacts before, CancellationToken token = default) => throw new NotSupportedException();

}
