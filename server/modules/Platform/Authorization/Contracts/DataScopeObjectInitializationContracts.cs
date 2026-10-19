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
    public sealed record Legacy(DataExecutionInternalBinding Caller, IReadOnlyList<string> ParentObjectIds, DataExecutionWriteFacts Target)
        : DataScopeObjectInitializationSource(Caller, ParentObjectIds, Target);
}

/// <summary>有限来源登记只引用同一正式执行的角色，不维护第二份数据动作要求。</summary>
public sealed record DataScopeObjectInitializationBinding(string ModuleId, string ResourceKey,
    DataExecutionInternalBinding Caller, string Stage, string ParentRole, string CandidateRole);

/// <summary>未迁移复制入口的显式旧执行身份；不允许缺声明的正式创建选择此来源。</summary>
public sealed record DataScopeObjectInitializationLegacyBinding(string ModuleId, string ResourceKey, DataExecutionInternalBinding Caller);

/// <summary>真实对象拥有者在创建事务内初始化直接授权；此契约不提供HTTP授权管理旁路。</summary>
public interface IDataScopeObjectInitializer
{
    /// <summary>
    /// 保留无来源旧ABI但明确拒绝；初始化必须使用下方携带真实来源的重载。
    /// 调用方负责完整父位置、父编辑及新对象创建资格，且必须持有业务事务和可信审计作用域。
    /// 不要求父授权管理资格或新对象读取资格；父配置为空时返回false且不记录授权变化。
    /// </summary>
    /// <param name="resourceKey">父和新对象共享的规范数据资源键。</param>
    /// <param name="parentObjectId">拥有者已核证的真实父对象EID，不接受关系LID。</param>
    /// <param name="newObjectId">当前事务刚创建、尚无直接授权配置的新对象EID。</param>
    /// <param name="token">当前业务操作的取消令牌。</param>
    Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId, CancellationToken token = default);

    /// <summary>
    /// 保留无来源旧ABI但明确拒绝；多父初始化必须使用下方携带真实来源的重载。
    /// 当前受控结构入口保证这些配置应相同；不相同按不变量失败关闭，不表示已提供交互选来源能力。
    /// </summary>
    Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        CancellationToken token = default);

    /// <summary>消费本次正式创建或明确旧复制来源后，在原事务和锁内复制父完整配置。</summary>
    Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default) => throw new NotSupportedException();

    /// <summary>保留全部规范化父集合的配置等价比较；来源核对不重复父权限裁决。</summary>
    Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default) => throw new NotSupportedException();
}
