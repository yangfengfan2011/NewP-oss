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
    IEntityAuditValueProvider audit, IDataExecutionAccess executions, IStandardCrudResourceRegistry registry,
    IEnumerable<DataScopeResource> declaredResources,
    IEnumerable<IEntityOwnershipTransferReceiver> receivers, IBusinessAuditRecorder recorder, ITransactionalAccountOwnershipDirectory? accounts = null)
{
    /// <summary>正式入口传入固定submit来源，锁后仍在原owner/edit位置独立裁决。</summary>
    public async ValueTask<EntityOwnershipTransferResult> TransferAsync(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        string recipientId, DataExecutionBindingReference source, CancellationToken token = default)
    {
        ValidateTransferSource(source, "submit", targets);
        return await TransferCoreAsync(targets, recipientId, (resource, id, cancellation) => BoundEditAsync(source, resource, id, cancellation), token);
    }

    // 原锁序、当前旧行、接收、审计与整批事务共用；只替换原edit时点的正式来源。
    private async ValueTask<EntityOwnershipTransferResult> TransferCoreAsync(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        string recipientId, Func<string, string, CancellationToken, Task<bool>> mayEdit, CancellationToken token)
    {
        if (targets.Count == 0 || targets.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey) || string.IsNullOrWhiteSpace(target.ObjectId))
            || string.IsNullOrWhiteSpace(recipientId)) throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        var receiver = receivers.SingleOrDefault() ?? throw new EntityOwnershipException(503, "EntityOwnership.ReceivingUnavailable");
        var selected = targets.Distinct().OrderBy(target => target.ResourceKey, StringComparer.Ordinal)
            .ThenBy(target => target.ObjectId, StringComparer.Ordinal).Select(target => (Target: target, Model: ResolveModel(target.ResourceKey))).ToArray();
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync(selected.Select(item => item.Model).Distinct(StringComparer.Ordinal).ToArray(), async cancellation =>
        {
            await receiver.PrepareAsync(cancellation);
            // 稳定顺序与现有归属补齐一致，真实行锁持有到整批接收与通知提交。
            foreach (var item in selected)
                if (await Rows(item.Model).Where(row => EF.Property<string>(row, "EID") == item.Target.ObjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "EID"), row => EF.Property<string>(row, "EID")), cancellation) != 1)
                    throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            var stamp = audit.Current();
            var current = new List<(EntityOwnershipTransferTarget Target, string Model, Dictionary<string, object?> Row)>();
            foreach (var group in selected.GroupBy(item => item.Model))
                foreach (var batch in group.Chunk(100))
                {
                    var ids = batch.Select(item => item.Target.ObjectId).ToArray();
                    var rows = await Rows(group.Key).AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EID"))).ToDictionaryAsync(row => (string)row["EID"]!, cancellation);
                    foreach (var item in batch)
                    {
                        var row = rows[item.Target.ObjectId];
                        if (!Equals(row.GetValueOrDefault(EntityOwnershipFields.OwnerId), stamp.ActorId)
                            || !await mayEdit(item.Target.ResourceKey, item.Target.ObjectId, cancellation))
                            throw new EntityOwnershipException(403, "EntityOwnership.TransferDenied");
                        current.Add((item.Target, item.Model, row));
                    }
                }
            var recipient = await (accounts ?? throw new EntityOwnershipException(503, "EntityOwnership.AccountDirectoryUnavailable")).FindAsync(recipientId, cancellation)
                ?? throw new EntityOwnershipException(409, "EntityOwnership.RecipientUnavailable");
            var changed = new List<EntityOwnershipTransferTarget>();
            foreach (var item in current)
            {
                var oldOwner = item.Row.GetValueOrDefault(EntityOwnershipFields.OwnerId) as string;
                var oldDepartment = item.Row.GetValueOrDefault(EntityOwnershipFields.OwnerDepartmentId) as string;
                if (oldOwner == recipient.AccountId && oldDepartment == recipient.DepartmentId) continue;
                await Rows(item.Model).Where(row => EF.Property<string>(row, "EID") == item.Target.ObjectId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => EF.Property<string?>(row, "OwnerId"), recipient.AccountId)
                    .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), recipient.DepartmentId)
                    .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), stamp.ActorId)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), stamp.OccurredAtUtc), cancellation);
                await recorder.RegisterAsync(new("entity.ownership.transferred", item.Target.ResourceKey, item.Target.ObjectId,
                    [new("OwnerId", BusinessAuditValue.Reference("user", oldOwner), BusinessAuditValue.Reference("user", recipient.AccountId)),
                     new("OwnerDepartmentId", BusinessAuditValue.Reference("department", oldDepartment), BusinessAuditValue.Reference("department", recipient.DepartmentId))]), cancellation);
                changed.Add(item.Target);
            }
            if (changed.Count > 0) await receiver.ReceiveAsync(recipient, changed, cancellation);
            return new EntityOwnershipTransferResult(true, changed.Count);
        }, token);
    }

    /// <summary>正式准备入口核固定prepare来源；后续submit不消费本次允许结果。</summary>
    public async ValueTask CheckAsync(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        DataExecutionBindingReference source, CancellationToken token = default)
    {
        ValidateTransferSource(source, "prepare", targets);
        await CheckCoreAsync(targets, (resource, id, cancellation) => BoundEditAsync(source, resource, id, cancellation), token);
    }

    // 规范目标与原owner拒绝先后不变；读取依赖由同一个edit裁决引擎完成。
    private async ValueTask CheckCoreAsync(IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        Func<string, string, CancellationToken, Task<bool>> mayEdit, CancellationToken token)
    {
        if (targets.Count == 0 || targets.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey) || string.IsNullOrWhiteSpace(target.ObjectId)))
            throw new EntityOwnershipException(400, "EntityOwnership.InvalidTransfer");
        if (!receivers.Any()) throw new EntityOwnershipException(503, "EntityOwnership.ReceivingUnavailable");
        var actor = audit.Current().ActorId;
        foreach (var group in targets.Distinct().Select(target => (Target: target, Model: ResolveModel(target.ResourceKey))).GroupBy(item => item.Model))
            foreach (var batch in group.Chunk(100))
            {
                var ids = batch.Select(item => item.Target.ObjectId).ToArray();
                var owners = await Rows(group.Key).AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EID")))
                    .Select(row => new { Id = EF.Property<string>(row, "EID"), Owner = EF.Property<string?>(row, "OwnerId") })
                    .ToDictionaryAsync(row => row.Id, token);
                foreach (var item in batch)
                {
                    if (!owners.TryGetValue(item.Target.ObjectId, out var row)) throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
                    if (row.Owner != actor || !await mayEdit(item.Target.ResourceKey, item.Target.ObjectId, token))
                        throw new EntityOwnershipException(403, "EntityOwnership.TransferDenied");
                }
            }
    }

    // 来源固定于执行端，真实对象仍在原时点传入共同对象角色执行器。
    private async Task<bool> BoundEditAsync(DataExecutionBindingReference source, string resource, string id, CancellationToken token)
    {
        await foreach (var decision in executions.EvaluateObjectsAsync(source, "target", [new(resource, id)], token))
            return decision.Decision == DataExecutionDecision.Allowed;
        throw new InvalidOperationException("Transfer target evaluation returned no decision.");
    }

    // 核实际HTTP声明及资源；prepare/submit由真实方法选择，客户端不能指定另一执行或阶段。
    private void ValidateTransferSource(DataExecutionBindingReference source, string stage, IReadOnlyCollection<EntityOwnershipTransferTarget> targets)
    {
        if (source.Provider != "entityOwnership.transfer" || source.Stage != stage)
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        foreach (var resource in targets.Select(target => target.ResourceKey).Distinct(StringComparer.Ordinal)) executions.Validate([source], resource);
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
