using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Ownership;

/// <summary>归属缺失列表只返回当前可读对象，不把账号归属补齐变成普通编辑或任意代转。</summary>
public sealed record EntityOwnershipPendingItem(string Id, string Name);
public sealed record EntityOwnershipPendingPage(IReadOnlyList<EntityOwnershipPendingItem> Items, int Total, int Page, int PageSize);
public sealed record EntityOwnershipState(bool Missing, string? OwnerId, string? OwnerDepartmentId);

/// <summary>供对象owner正式入口复用的归属维护核心；资源、模型和准入由已登记descriptor决定。</summary>
public sealed class EntityOwnershipMaintenance(EfPlatformDbContext db, IDataScopeReadPlanProvider reads,
    IDataScopeObjectDecisionProvider decisions, IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit,
    EntityOwnershipBackfill backfill)
{
    /// <summary>SQL先组合读取权限及两种缺失，再Count和稳定分页；不按已加载页面内存筛选。</summary>
    public async Task<EntityOwnershipPendingPage> PendingAsync(StandardCrudDescriptor descriptor, int page, int pageSize,
        string? keyword, CancellationToken token)
    {
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        page = page <= 0 ? 1 : page;
        if (page > int.MaxValue / pageSize) throw new EntityOwnershipException(400, "EntityOwnership.InvalidPage");
        var rows = Rows(descriptor).AsNoTracking();
        var read = await reads.GetReadPlanAsync(descriptor.ResourceKey, token);
        if (read.Predicate is not null) rows = rows.Where(read.Predicate);
        rows = rows.Where(row => EF.Property<string?>(row, "OwnerId") == null || EF.Property<string?>(row, "OwnerDepartmentId") == null);
        if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(row => EF.Property<string>(row, "ENAME").Contains(keyword));
        var total = await rows.CountAsync(token);
        var items = await rows.OrderBy(row => EF.Property<string>(row, "ENAME")).ThenBy(row => EF.Property<string>(row, "EID"))
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new EntityOwnershipPendingItem(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"))).ToArrayAsync(token);
        return new(items, total, page, pageSize);
    }

    /// <summary>对象可读及edit资格同样适用于补齐状态；不可读或不存在保持同一404外观。</summary>
    public async Task<EntityOwnershipState> ReadAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token)
    {
        await RequireAccessAsync(descriptor, id, token);
        var row = await Rows(descriptor).AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == id, token)
            ?? throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
        return new(row.GetValueOrDefault("OwnerId") is null || row.GetValueOrDefault("OwnerDepartmentId") is null,
            row.GetValueOrDefault("OwnerId") as string, row.GetValueOrDefault("OwnerDepartmentId") as string);
    }

    /// <summary>补齐和事件共享真实业务作用域、事务及锁后资格，不触发普通对象更新或版本快照重建。</summary>
    public async ValueTask<bool> InitializeAsync(StandardCrudDescriptor descriptor, string id, string? accountId, CancellationToken token)
    {
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync([descriptor.ModelName], async cancellation =>
        {
            await RequireAccessAsync(descriptor, id, cancellation);
            return await backfill.InitializeAsync(descriptor.ModelName, descriptor.ResourceKey, id, accountId, cancellation);
        }, token);
    }

    /// <summary>一份明确选择同事务重验并自动补齐；未知账号保留待人工，资格及写入失败不部分提交。</summary>
    public async ValueTask<EntityOwnershipBatchResult> InitializeManyAsync(StandardCrudDescriptor descriptor, string[] ids, CancellationToken token)
    {
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new EntityOwnershipException(400, "EntityOwnership.InvalidSelection");
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync([descriptor.ModelName], async cancellation =>
        {
            foreach (var id in ids.Order(StringComparer.Ordinal)) await RequireAccessAsync(descriptor, id, cancellation);
            return await backfill.InitializeManyAsync(descriptor.ModelName, descriptor.ResourceKey, ids, cancellation);
        }, token);
    }

    // 已登记标准对象沿原read/edit裁决，不信任客户端所称归属；拥有者额外强制范围由自己的入口负责。
    private async Task RequireAccessAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token)
    {
        if (!await decisions.IsAllowedAsync(descriptor.ResourceKey, id, "read", token))
            throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
        if (!await decisions.IsAllowedAsync(descriptor.ResourceKey, id, "edit", token))
            throw new EntityOwnershipException(403, "EntityOwnership.EditDenied");
    }

    // 只允许正式descriptor登记的模型进入通用持久化，不接收物理表名或字段表达式。
    private IQueryable<Dictionary<string, object?>> Rows(StandardCrudDescriptor descriptor) =>
        db.Set<Dictionary<string, object?>>(db.ResolveModelKey(descriptor.ModelName));
}
