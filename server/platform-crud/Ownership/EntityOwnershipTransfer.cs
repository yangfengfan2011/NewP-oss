using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modeling.Ownership;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Ownership;

/// <summary>成功结果不依赖转交后回读，不携带对象名称、来源或接收人权限保证。</summary>
public sealed record EntityOwnershipTransferResult(bool Succeeded, int Changed);

/// <summary>规范对象转交只改变归属；接收、记录与结果共享原数据库事务。</summary>
public sealed class EntityOwnershipTransfer(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IEntityAuditValueProvider audit, IStandardCrudResourceRegistry registry,
    IEnumerable<DataScopeResource> declaredResources,
    IEnumerable<IEntityOwnershipTransferReceiver> receivers, IBusinessAuditRecorder recorder, ITransactionalAccountOwnershipDirectory? accounts = null)
{
    /// <summary>原接收协调及对象行锁内取得当前所属人事实，平台在接收账号读取前检查本次按钮的同一目标。</summary>
    public async Task<TResult> WithinTransferAsync<TResult>(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        string recipientId, Func<EntityOwnershipTransferState, CancellationToken, Task<TResult>> execute, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(recipientId)) throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        var selected = ResolveSelection(targets);
        var receiver = Receiver();
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync(selected.Select(item => item.Model).Distinct(StringComparer.Ordinal).ToArray(), async cancellation =>
        {
            await receiver.PrepareAsync(cancellation);
            // 行锁次序不因模块入口或按钮配置改变，全部事实保留到归属、接收及通知共同提交。
            foreach (var item in selected)
                if (await Rows(item.Model).Where(row => EF.Property<string>(row, "EID") == item.Target.ObjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "EID"), row => EF.Property<string>(row, "EID")), cancellation) != 1)
                    throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            var state = new EntityOwnershipTransferState(this, await ReadOwnedFactsAsync(selected, cancellation),
                recipientId, db.Database.CurrentTransaction!.TransactionId, receiver);
            try { return await execute(state, cancellation); }
            finally { state.End(); }
        }, token);
    }

    /// <summary>实际准备请求重新取得当前所属人及完整对象事实；不持有提交允许结果或增加提交图锁。</summary>
    public async Task<TResult> WithinPreparationAsync<TResult>(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        Func<EntityOwnershipTransferState, CancellationToken, Task<TResult>> execute, CancellationToken token = default)
    {
        var selected = ResolveSelection(targets);
        var receiver = Receiver();
        var state = new EntityOwnershipTransferState(this, await ReadOwnedFactsAsync(selected, token), null, null, receiver);
        try { return await execute(state, token); }
        finally { state.End(); }
    }

    /// <summary>只消费原锁内状态一次；平台已检查按钮，接收人当前事实、归属、审计与接收仍由业务完成。</summary>
    public async Task<EntityOwnershipTransferResult> CommitAsync(EntityOwnershipTransferState state, CancellationToken token = default)
    {
        state.Consume(this, db.Database.CurrentTransaction?.TransactionId);
        var recipient = await (accounts ?? throw new EntityOwnershipException(503, "EntityOwnership.AccountDirectoryUnavailable"))
            .FindAsync(state.RecipientId!, token) ?? throw new EntityOwnershipException(409, "EntityOwnership.RecipientUnavailable");
        var stamp = audit.Current();
        var changed = new List<EntityOwnershipTransferTarget>();
        foreach (var item in state.Objects)
        {
            var oldOwner = item.Before.GetValueOrDefault(EntityOwnershipFields.OwnerId) as string;
            var oldDepartment = item.Before.GetValueOrDefault(EntityOwnershipFields.OwnerDepartmentId) as string;
            if (oldOwner == recipient.AccountId && oldDepartment == recipient.DepartmentId) continue;
            await Rows(item.ObjectName).Where(row => EF.Property<string>(row, "EID") == item.ObjectId).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => EF.Property<string?>(row, "OwnerId"), recipient.AccountId)
                .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), recipient.DepartmentId)
                .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), stamp.ActorId)
                .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), stamp.OccurredAtUtc), token);
            await recorder.RegisterAsync(new("entity.ownership.transferred", item.ResourceKey, item.ObjectId,
                [new("OwnerId", BusinessAuditValue.Reference("user", oldOwner), BusinessAuditValue.Reference("user", recipient.AccountId)),
                 new("OwnerDepartmentId", BusinessAuditValue.Reference("department", oldDepartment), BusinessAuditValue.Reference("department", recipient.DepartmentId))]), token);
            changed.Add(new(item.ResourceKey, item.ObjectId));
        }
        if (changed.Count > 0) await state.Receiver.ReceiveAsync(recipient, changed, token);
        return new(true, changed.Count);
    }

    // 模型来自正式注册；选择去重和锁序只由真实业务对象决定，不包含接收账号。
    private (EntityOwnershipTransferTarget Target, string Model)[] ResolveSelection(IReadOnlyCollection<EntityOwnershipTransferTarget> targets)
    {
        if (targets.Count == 0 || targets.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey) || string.IsNullOrWhiteSpace(target.ObjectId)))
            throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        return targets.Distinct().OrderBy(target => target.ResourceKey, StringComparer.Ordinal).ThenBy(target => target.ObjectId, StringComparer.Ordinal)
            .Select(target => (target, ResolveModel(target.ResourceKey))).ToArray();
    }

    // 缺接收能力保持操作级503，不通过DI缺失破坏独立模块原启动。
    private IEntityOwnershipTransferReceiver Receiver() => receivers.SingleOrDefault()
        ?? throw new EntityOwnershipException(503, "EntityOwnership.ReceivingUnavailable");

    // 当前所属人是独立业务资格；读取完整旧事实供平台一次判权及后续真实diff共用。
    private async Task<IReadOnlyList<EntityOwnershipTransferObject>> ReadOwnedFactsAsync(
        IReadOnlyList<(EntityOwnershipTransferTarget Target, string Model)> selected, CancellationToken token)
    {
        var actor = audit.Current().ActorId;
        var facts = new List<EntityOwnershipTransferObject>();
        foreach (var group in selected.GroupBy(item => item.Model))
            foreach (var batch in group.Chunk(100))
            {
                var ids = batch.Select(item => item.Target.ObjectId).ToArray();
                var rows = await Rows(group.Key).AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EID")))
                    .ToDictionaryAsync(row => (string)row["EID"]!, token);
                foreach (var item in batch)
                {
                    if (!rows.TryGetValue(item.Target.ObjectId, out var row)) throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
                    if (!Equals(row.GetValueOrDefault(EntityOwnershipFields.OwnerId), actor))
                        throw new EntityOwnershipException(403, "EntityOwnership.TransferDenied");
                    facts.Add(new(item.Target.ResourceKey, item.Model, item.Target.ObjectId, row));
                }
            }
        return facts;
    }

    // 只消费真实 owner 已登记的规范资源；选用归属字段的模型才可转交，不引入业务模块白名单。
    private string ResolveModel(string resourceKey)
    {
        var descriptor = registry.FindByResourceKey(resourceKey);
        var declared = declaredResources.SingleOrDefault(resource => resource.ResourceKey == resourceKey);
        var modelKey = descriptor?.ModelName ?? declared?.ModelKey ?? throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        if ((descriptor?.IdField ?? declared?.IdField) != "EID") throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        var model = db.PersistencePlan.Models.Single(model => model.LogicalKey == modelKey);
        if (!model.Fields.Any(field => field.Name == EntityOwnershipFields.OwnerId) || !model.Fields.Any(field => field.Name == EntityOwnershipFields.OwnerDepartmentId))
            throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        return modelKey;
    }

    // 物理模型只来自注册的 owner 描述，不接收客户端表名或字段表达式。
    private IQueryable<Dictionary<string, object?>> Rows(string modelKey) => db.Set<Dictionary<string, object?>>(modelKey);
}
