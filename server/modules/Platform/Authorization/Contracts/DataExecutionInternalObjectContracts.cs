namespace ICP.Platform.Authorization.Contracts;

public partial interface IDataExecutionAccess
{
    /// <summary>核对内部固定来源后执行同一对象角色要求，每次消费当前阶段的真实目标与读取事实。</summary>
    Task<DataExecutionTargetDecision> EvaluateInternalObjectAsync(DataExecutionInternalBinding binding, string role,
        DataExecutionTargetInput target, CancellationToken cancellationToken = default);
}
