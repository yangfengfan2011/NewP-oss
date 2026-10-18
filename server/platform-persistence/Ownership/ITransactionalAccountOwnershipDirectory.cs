namespace ICP.Platform.Persistence.Ownership;

/// <summary>转交在现有业务事务内读取账号与当前直接部门，不消费候选或已发布组织缓存。</summary>
public interface ITransactionalAccountOwnershipDirectory
{
    /// <summary>读取本事务当前事实；不存在或已删除账号返回空，停用账号与空部门沿既有归属合同保留。</summary>
    ValueTask<AccountOwnership?> FindAsync(string accountId, CancellationToken cancellationToken = default);
}
