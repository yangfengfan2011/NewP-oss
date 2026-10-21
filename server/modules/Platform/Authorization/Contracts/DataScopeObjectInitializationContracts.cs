namespace ICP.Platform.Authorization.Contracts;

/// <summary>受控owner取得实际采用的父对象事实；此结果不作为HTTP回执公开。</summary>
public sealed record DataScopeObjectInitialization(bool Initialized, string ParentObjectId);

/// <summary>当前创建事实证明的不透明引用；只有运行时认可的私有实现及原scope/事务才有效，不表示权限允许。</summary>
public abstract class DataScopeObjectCreationProof
{
    // 实现与Contracts属于不同程序集；派生能力不代表可信，Sources必须拒绝未知实现。
    protected DataScopeObjectCreationProof() { }
}

/// <summary>初始化只核对本次创建的真实参与对象，不携带允许结果或替代父权限裁决。</summary>
public abstract record DataScopeObjectInitializationSource(IReadOnlyList<string> ParentObjectIds, DataExecutionWriteFacts Target)
{
    /// <summary>当前活动事务中的真实创建目标；只证明创建事实，不表示父或候选已获准。</summary>
    /// <remarks>无Proof只接受Added；结构图需在首次Flush前捕获真实Entry，并逐次核对原事务和当前完整事实。</remarks>
    public sealed record Created(IReadOnlyList<string> ParentObjectIds, DataExecutionWriteFacts Target)
        : DataScopeObjectInitializationSource(ParentObjectIds, Target)
    {
        /// <summary>结构创建在首次Flush前捕获；缺省仍只接受当前Added目标，不扩大普通创建来源。</summary>
        public DataScopeObjectCreationProof? Proof { get; init; }
    }
    /// <summary>内部新父及目标来自同一实际创建图，Flush后仍逐次核对当前事实与原Entry引用。</summary>
    public sealed record CreatedFromCreatedParents(IReadOnlyList<DataExecutionWriteFacts> Parents,
        DataExecutionWriteFacts Target, DataScopeObjectCreationProof Proof)
        : DataScopeObjectInitializationSource(Parents.Select(parent => parent.ObjectId).ToArray(), Target);
}

/// <summary>真实对象拥有者在创建事务内初始化直接授权；此契约不提供HTTP授权管理旁路。</summary>
public interface IDataScopeObjectInitializer
{
    /// <summary>只从当前事务的真实Added行捕获创建参与者；后续Flush不把它变成权限或跨事务凭证。</summary>
    DataScopeObjectCreationProof CaptureCreatedObjects(string resourceKey, IReadOnlyList<DataExecutionWriteFacts> objects) =>
        throw new InvalidOperationException("DATA_SCOPE_CREATION_PROOF_UNSUPPORTED");

    /// <summary>消费本次正式创建来源后，在原事务和锁内复制父完整配置。</summary>
    Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default);

    /// <summary>保留全部规范化父集合的配置等价比较；来源核对不重复父权限裁决。</summary>
    Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        DataScopeObjectInitializationSource source, CancellationToken token = default);
}
