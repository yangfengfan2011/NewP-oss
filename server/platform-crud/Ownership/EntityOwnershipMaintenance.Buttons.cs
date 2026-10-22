using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Descriptors;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Ownership;

public sealed partial class EntityOwnershipMaintenance
{
    /// <summary>独立状态请求读取实际对象事实；不返回后续提交可复用的许可或可写状态。</summary>
    public async Task<TResult> WithinStatusAsync<TResult>(StandardCrudDescriptor descriptor, string id,
        Func<EntityOwnershipInitializationState, CancellationToken, Task<TResult>> execute, CancellationToken token)
    {
        var state = new EntityOwnershipInitializationState(this, descriptor, [id], null);
        try { return await execute(state, token); }
        finally { state.End(); }
    }

    /// <summary>原事务内规范化明确选择并由平台检查，随后仍由原补齐核心取得行锁及重读当前事实。</summary>
    public async Task<TResult> WithinInitializationAsync<TResult>(StandardCrudDescriptor descriptor, string[] ids,
        Func<EntityOwnershipInitializationState, CancellationToken, Task<TResult>> execute, CancellationToken token)
    {
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new EntityOwnershipException(400, "EntityOwnership.InvalidSelection");
        var selection = ids.Order(StringComparer.Ordinal).ToArray();
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync([descriptor.ModelName], async cancellation =>
        {
            // 维护保留原先判权再进入Backfill行锁的时点；拒绝请求不提前写EID或持有这些对象锁。
            var state = new EntityOwnershipInitializationState(this, descriptor, selection, db.Database.CurrentTransaction!.TransactionId);
            try { return await execute(state, cancellation); }
            finally { state.End(); }
        }, token);
    }

    /// <summary>人工单对象保留未知账号409及原NULL-only规则；原按钮检查之后才进入补齐行锁，不重复固定edit。</summary>
    public async Task<bool> InitializeOneAsync(EntityOwnershipInitializationState state, string? accountId, CancellationToken token)
    {
        state.Consume(this, db.Database.CurrentTransaction?.TransactionId);
        if (state.Objects.Count != 1) throw new InvalidOperationException("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID");
        return await backfill.InitializeAsync(state.Descriptor.ModelName, state.Descriptor.ResourceKey, state.Objects[0].ObjectId, accountId, token);
    }

    /// <summary>自动批次只消费已检查的原选择，未知账号仍计为待人工，不改写已有归属及四审计字段。</summary>
    public async Task<EntityOwnershipBatchResult> InitializeSelectedAsync(EntityOwnershipInitializationState state, CancellationToken token)
    {
        state.Consume(this, db.Database.CurrentTransaction?.TransactionId);
        return await backfill.InitializeManyAsync(state.Descriptor.ModelName, state.Descriptor.ResourceKey,
            state.Objects.Select(item => item.ObjectId).ToArray(), token);
    }

    /// <summary>逐项执行原业务read并取得该行事实；yield后平台检查当前按钮，再推进下一项。</summary>
    public async IAsyncEnumerable<EntityOwnershipInitializationObject> PrepareTargetsAsync(EntityOwnershipInitializationState state,
        [EnumeratorCancellation] CancellationToken token)
    {
        state.BeginPreparation(this, db.Database.CurrentTransaction?.TransactionId);
        foreach (var id in state.Selection)
        {
            var target = await PrepareInitializationTargetAsync(state, id, token);
            state.AddPrepared(target);
            yield return target;
        }
        state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
        state.CompletePreparation();
    }

    // 不预读后项存在性；原visibility是独立业务read，不能由按钮edit的可变RequiresRead设置代替。
    private async Task<EntityOwnershipInitializationObject> PrepareInitializationTargetAsync(EntityOwnershipInitializationState state,
        string id, CancellationToken token)
    {
        try
        {
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            // 原维护将未知目标遮蔽为404；只有本资源正式EID才进入通用对象访问检查，Bind错误仍在外层保持400。
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(state.Descriptor.ModelName + "_", StringComparison.Ordinal))
                throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            if (await objectAccess.CheckAsync(state.Descriptor.ModelName, id, ["read"], token) != DataExecutionDecision.Allowed)
                throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            var row = await Rows(state.Descriptor).AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == id, token)
                ?? throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            return new(state.Descriptor.ResourceKey, state.Descriptor.ModelName, id, row);
        }
        catch { state.FailPreparation(); throw; }
    }
}
