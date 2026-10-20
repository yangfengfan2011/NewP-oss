namespace ICP.Platform.Authorization.Contracts;

/// <summary>受控owner取得实际采用的父对象事实；此结果不作为HTTP回执公开。</summary>
public sealed record DataScopeObjectInitialization(bool Initialized, string ParentObjectId);

/// <summary>初始化只核对本次创建的真实参与对象，不携带允许结果或替代父权限裁决。</summary>
public abstract record DataScopeObjectInitializationSource(DataExecutionInternalBinding Caller,
    IReadOnlyList<string> ParentObjectIds, DataExecutionWriteFacts Target)
{
    public sealed record Declared(DataExecutionInternalBinding Caller, DataExecutionBindingReference Binding,
        IReadOnlyList<string> ParentObjectIds, DataExecutionWriteFacts Target)
        : DataScopeObjectInitializationSource(Caller, ParentObjectIds, Target);
    /// <summary>受控结构创建的真实新父候选事实；不表示这些新对象已被读取或获准编辑。</summary>
    public sealed record DeclaredFromCreatedParents(DataExecutionInternalBinding Caller, DataExecutionBindingReference Binding,
        IReadOnlyList<DataExecutionWriteFacts> Parents, DataExecutionWriteFacts Target)
        : DataScopeObjectInitializationSource(Caller, Parents.Select(parent => parent.ObjectId).ToArray(), Target);

}

/// <summary>有限来源登记只引用同一正式执行的角色，不维护第二份数据动作要求。</summary>
public sealed record DataScopeObjectInitializationBinding(string ModuleId, string ResourceKey,
    DataExecutionInternalBinding Caller, string Stage, string ParentRole, string CandidateRole);

/// <summary>只为实际结构创建者登记新图父事实来源，复用其原候选角色，不定义第二份权限要求。</summary>
public sealed record DataScopeObjectInitializationCreatedParentsBinding(string ModuleId, string ResourceKey,
    DataExecutionInternalBinding Caller, string Stage, string CandidateRole);

/// <summary>真实对象拥有者在创建事务内初始化直接授权；此契约不提供HTTP授权管理旁路。</summary>
public interface IDataScopeObjectInitializer
{
    /// <summary>消费本次正式创建来源后，在原事务和锁内复制父完整配置。</summary>
    Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default);

    /// <summary>保留全部规范化父集合的配置等价比较；来源核对不重复父权限裁决。</summary>
    Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default);
}
