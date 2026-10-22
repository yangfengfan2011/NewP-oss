using ICP.Platform.Persistence.Ownership;

namespace ICP.Platform.Crud.Ownership;

/// <summary>已由真实对象入口解析的规范目标；关系 LID 和来源位置不构成对象身份。</summary>
public sealed record EntityOwnershipTransferTarget(string ResourceKey, string ObjectId);

/// <summary>在原转交事务中登记接收目录、原对象引用及成功通知；不独立提交。</summary>
public interface IEntityOwnershipTransferReceiver
{
    /// <summary>在对象行锁之前取得接收端既有事务协调权；不得写入或独立提交。</summary>
    ValueTask PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接收核心已在锁内验证当前所属人及 edit 的对象集合。
    /// recipient 是同一事务中读取的可信账号事实，部门可空；操作者和时间沿原审计作用域取得。
    /// 不将接收者 read 作为前置，也不授予对象读取权限或修改手工 ACL。
    /// </summary>
    ValueTask ReceiveAsync(AccountOwnership recipient, IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        CancellationToken cancellationToken = default);
}
