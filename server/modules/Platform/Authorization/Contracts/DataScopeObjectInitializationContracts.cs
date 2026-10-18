namespace ICP.Platform.Authorization.Contracts;

/// <summary>受控owner取得实际采用的父对象事实；此结果不作为HTTP回执公开。</summary>
public sealed record DataScopeObjectInitialization(bool Initialized, string ParentObjectId);

/// <summary>真实对象拥有者在创建事务内初始化直接授权；此契约不提供HTTP授权管理旁路。</summary>
public interface IDataScopeObjectInitializer
{
    /// <summary>
    /// 将明确父对象的一份直接配置复制到同资源的新对象，并生成独立技术行身份。
    /// 调用方负责完整父位置、父编辑及新对象创建资格，且必须持有业务事务和可信审计作用域。
    /// 不要求父授权管理资格或新对象读取资格；父配置为空时返回false且不记录授权变化。
    /// </summary>
    /// <param name="resourceKey">父和新对象共享的规范数据资源键。</param>
    /// <param name="parentObjectId">拥有者已核证的真实父对象EID，不接受关系LID。</param>
    /// <param name="newObjectId">当前事务刚创建、尚无直接授权配置的新对象EID。</param>
    /// <param name="token">当前业务操作的取消令牌。</param>
    Task<bool> InitializeFromParentAsync(string resourceKey, string parentObjectId, string newObjectId, CancellationToken token = default);

    /// <summary>
    /// 锁定并比较全部已核证父对象的完整语义；相同才为新对象初始化一次，技术行ID不参与比较。
    /// 当前受控结构入口保证这些配置应相同；不相同按不变量失败关闭，不表示已提供交互选来源能力。
    /// </summary>
    Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resourceKey, IReadOnlyList<string> parentObjectIds, string newObjectId,
        CancellationToken token = default);
}
