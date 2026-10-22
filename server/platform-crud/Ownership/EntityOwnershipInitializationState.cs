using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Ownership;

/// <summary>原维护判权时点的实际对象及前事实；补齐核心随后在原行锁内重读，不使用此快照写值。</summary>
public sealed record EntityOwnershipInitializationObject(string ResourceKey, string ObjectName, string ObjectId,
    IReadOnlyDictionary<string, object?> Before);

/// <summary>仅在原维护调用和事务内有效的业务状态，状态读取不能据此发起写入。</summary>
public sealed class EntityOwnershipInitializationState
{
    private readonly EntityOwnershipMaintenance owner;
    private readonly Guid? transactionId;
    private bool active = true;
    private bool consumed;
    private bool preparing;
    private bool prepared;
    private readonly List<EntityOwnershipInitializationObject> objects = [];
    internal bool PreparationFailed { get; private set; }
    internal IReadOnlyList<string> Selection { get; }
    internal StandardCrudDescriptor Descriptor { get; }
    public IReadOnlyList<EntityOwnershipInitializationObject> Objects => objects;

    // 原业务边界先冻结实际选择，事实随后逐项读取；不承载允许结果。
    internal EntityOwnershipInitializationState(EntityOwnershipMaintenance owner, StandardCrudDescriptor descriptor,
        IReadOnlyList<string> selection, Guid? transactionId)
    {
        this.owner = owner; Descriptor = descriptor; Selection = selection.ToArray(); this.transactionId = transactionId;
    }

    // 逐项规范化只能在原保护回调内开始一次，不预读下一项事实，也不保存允许结果。
    internal void BeginPreparation(EntityOwnershipMaintenance currentOwner, Guid? currentTransaction)
    {
        RequireActive(currentOwner, currentTransaction);
        if (preparing) throw new InvalidOperationException("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID");
        preparing = true;
    }

    // 异步读取两侧均核原生命周期，不能在回调释放后继续读取或构造正式目标。
    internal void RequireActive(EntityOwnershipMaintenance currentOwner, Guid? currentTransaction)
    {
        if (!active || consumed || !ReferenceEquals(owner, currentOwner) || transactionId is not null && transactionId != currentTransaction)
            throw new InvalidOperationException("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID");
    }

    // 仅保留实际规范事实；完成标记是完整选择已解析，不是平台允许凭证。
    internal void AddPrepared(EntityOwnershipInitializationObject item) => objects.Add(item);
    // 正常枚举结束才允许业务消费全部实际选择，提前Dispose不能转成提交。
    internal void CompletePreparation() => prepared = objects.Count == Selection.Count;
    // 区分原业务读取故障与yield之后的平台目标拒绝，异常本身原样传播。
    internal void FailPreparation() => PreparationFailed = true;

    // 写入只消费同一服务、原事务和当前调用内的状态一次，禁止准备状态或跨事务重放。
    internal void Consume(EntityOwnershipMaintenance currentOwner, Guid? currentTransaction)
    {
        if (!active || consumed || !prepared || !ReferenceEquals(owner, currentOwner) || transactionId is null || transactionId != currentTransaction)
            throw new InvalidOperationException("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID");
        consumed = true;
    }

    // 无论正常返回、取消或故障，逃逸状态均不能再提交。
    internal void End() => active = false;
}
