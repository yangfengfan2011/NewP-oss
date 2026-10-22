namespace ICP.Platform.Crud.Ownership;

/// <summary>同一转交状态的规范业务对象与执行前事实；不含接收人目标或允许结果。</summary>
public sealed record EntityOwnershipTransferObject(string ResourceKey, string ObjectName, string ObjectId,
    IReadOnlyDictionary<string, object?> Before);

/// <summary>只在所属准备或原提交事务回调内有效，提交仅消费一次原状态。</summary>
public sealed class EntityOwnershipTransferState
{
    private readonly EntityOwnershipTransfer _owner;
    private readonly Guid? _transactionId;
    private bool _active = true;
    private bool _consumed;

    // 只能由核心从当前真实对象和原事务建立；模块输入不能构造证明。
    internal EntityOwnershipTransferState(EntityOwnershipTransfer owner, IReadOnlyList<EntityOwnershipTransferObject> objects,
        string? recipientId, Guid? transactionId, IEntityOwnershipTransferReceiver receiver)
    {
        _owner = owner; Objects = objects; RecipientId = recipientId; _transactionId = transactionId; Receiver = receiver;
    }

    public IReadOnlyList<EntityOwnershipTransferObject> Objects { get; }
    internal string? RecipientId { get; }
    internal IEntityOwnershipTransferReceiver Receiver { get; }

    // 准备、跨事务、失效或重复状态不能执行接收；这不是权限允许凭据。
    internal void Consume(EntityOwnershipTransfer owner, Guid? transactionId)
    {
        if (!_active || _consumed || !ReferenceEquals(owner, _owner) || _transactionId is null
            || transactionId != _transactionId || RecipientId is null)
            throw new InvalidOperationException("ENTITY_OWNERSHIP_TRANSFER_STATE_INVALID");
        _consumed = true;
    }

    // 退出业务回调后立即失效，不能在释放锁后继续提交。
    internal void End() => _active = false;
}
