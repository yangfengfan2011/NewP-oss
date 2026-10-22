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
public sealed partial class EntityOwnershipMaintenance(EfPlatformDbContext db, IDataExecutionAccess executions,
    IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit,
    EntityOwnershipBackfill backfill, IObjectActionAccess objectAccess)
{
    /// <summary>SQL先组合读取权限及两种缺失，再Count和稳定分页；不按已加载页面内存筛选。</summary>
    public async Task<EntityOwnershipPendingPage> PendingAsync(StandardCrudDescriptor descriptor, int page, int pageSize,
        string? keyword, DataExecutionBindingReference source, CancellationToken token)
    {
        ValidateSource(descriptor, source, "read");
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        page = page <= 0 ? 1 : page;
        if (page > int.MaxValue / pageSize) throw new EntityOwnershipException(400, "EntityOwnership.InvalidPage");
        var rows = Rows(descriptor).AsNoTracking();
        var read = await executions.GetQueryReadPlanAsync(new(source, "query"), descriptor.ResourceKey, token);
        if (read.Predicate is not null) rows = rows.Where(read.Predicate);
        rows = rows.Where(row => EF.Property<string?>(row, "OwnerId") == null || EF.Property<string?>(row, "OwnerDepartmentId") == null);
        if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(row => EF.Property<string>(row, "ENAME").Contains(keyword));
        var total = await rows.CountAsync(token);
        var items = await rows.OrderBy(row => EF.Property<string>(row, "ENAME")).ThenBy(row => EF.Property<string>(row, "EID"))
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new EntityOwnershipPendingItem(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"))).ToArrayAsync(token);
        return new(items, total, page, pageSize);
    }

    // 待补齐列表保留原独立query/read合同，不能以目标按钮替代读取来源。
    private void ValidateSource(StandardCrudDescriptor descriptor, DataExecutionBindingReference source, string stage)
    {
        if (source.Provider != "entityOwnership.initialize" || source.Stage != stage)
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        executions.Validate([source], descriptor.ResourceKey);
    }

    // 只允许正式descriptor登记的模型进入通用持久化，不接收物理表名或字段表达式。
    private IQueryable<Dictionary<string, object?>> Rows(StandardCrudDescriptor descriptor) =>
        db.Set<Dictionary<string, object?>>(db.ResolveModelKey(descriptor.ModelName));
}
