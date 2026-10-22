using ICP.Platform.Modeling.Ownership;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Ownership;

/// <summary>仅初始化已选模型的缺失归属，不改写旧审计、不提供已完整对象的代转出口。</summary>
public sealed class EntityOwnershipBackfill(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IAccountOwnershipDirectory directory, EntityOwnershipAudit audit)
{
    /// <summary>真实对象拥有者已核完整访问范围后，在同一事务及对象锁下补齐缺失值。</summary>
    public async ValueTask<bool> InitializeAsync(string modelName, string resource, string id, string? selectedAccountId,
        CancellationToken cancellationToken = default)
    {
        var result = await InitializeCoreAsync(modelName, resource, [id], selectedAccountId, cancellationToken);
        if (result.Unresolved > 0) throw new EntityOwnershipException(409, "EntityOwnership.AccountUnresolved");
        return result.Changed > 0;
    }

    /// <summary>显式对象集合自动补齐；未知账号或部门保留缺失并计数，资格或写入失败由外层事务整体回滚。</summary>
    public ValueTask<EntityOwnershipBatchResult> InitializeManyAsync(string modelName, string resource, IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default) => InitializeCoreAsync(modelName, resource, ids, null, cancellationToken);

    // 按稳定身份次序持有对象锁，每批只物化100行及其账号事实；100是I/O批大小，不是业务对象数量上限。
    private async ValueTask<EntityOwnershipBatchResult> InitializeCoreAsync(string modelName, string resource, IReadOnlyCollection<string> ids,
        string? selectedAccountId, CancellationToken cancellationToken)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("ENTITY_OWNERSHIP_TRANSACTION_REQUIRED");
        var model = db.PersistencePlan.Models.Single(model => model.LogicalKey == modelName);
        if (!model.Fields.Any(field => field.Name == EntityOwnershipFields.OwnerId)
            || !model.Fields.Any(field => field.Name == EntityOwnershipFields.OwnerDepartmentId))
            throw new InvalidOperationException("ENTITY_OWNERSHIP_NOT_SELECTED");
        var rows = db.Set<Dictionary<string, object?>>(modelName);
        var changed = 0; var unresolved = 0;
        foreach (var batch in ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Chunk(100))
        {
            foreach (var id in batch)
                if (await rows.Where(row => EF.Property<string>(row, "EID") == id).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => EF.Property<string>(row, "EID"), row => EF.Property<string>(row, "EID")), cancellationToken) != 1)
                    throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable");
            var existing = await rows.AsNoTracking().Where(row => batch.Contains(EF.Property<string>(row, "EID"))).ToArrayAsync(cancellationToken);
            var accountIds = existing.Select(row => row.GetValueOrDefault("OwnerId") as string ?? selectedAccountId ?? row.GetValueOrDefault("CreatedBy") as string)
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            var accounts = await directory.FindManyAsync(accountIds, cancellationToken);
            foreach (var row in existing)
            {
                var outcome = await InitializeRowAsync(modelName, resource, row, accounts, selectedAccountId, cancellationToken);
                changed += outcome.Changed; unresolved += outcome.Unresolved;
            }
        }
        return new(changed, unresolved);
    }

    // 仅更新缺失列；ExecuteUpdate有意跳过普通保存的四审计字段重算，实际diff与数据仍同事务。
    private async ValueTask<EntityOwnershipBatchResult> InitializeRowAsync(string modelName, string resource, Dictionary<string, object?> existing,
        IReadOnlyDictionary<string, AccountOwnership> accounts, string? selectedAccountId, CancellationToken cancellationToken)
    {
        var id = (string)existing["EID"]!;
        var owner = existing.GetValueOrDefault(EntityOwnershipFields.OwnerId) as string;
        var department = existing.GetValueOrDefault(EntityOwnershipFields.OwnerDepartmentId) as string;
        if (owner is not null && department is not null) return new(0, 0);
        if (owner is not null && selectedAccountId is not null && owner != selectedAccountId)
            throw new EntityOwnershipException(409, "EntityOwnership.AlreadyOwned");
        var accountId = owner ?? selectedAccountId ?? existing.GetValueOrDefault("CreatedBy") as string;
        if (string.IsNullOrWhiteSpace(accountId) || !accounts.TryGetValue(accountId, out var account) || string.IsNullOrWhiteSpace(account.DepartmentId))
            return new(0, 1);
        var after = new Dictionary<string, object?>(existing, StringComparer.Ordinal)
        {
            [EntityOwnershipFields.OwnerId] = owner ?? account.AccountId,
            [EntityOwnershipFields.OwnerDepartmentId] = department ?? account.DepartmentId
        };
        await db.Set<Dictionary<string, object?>>(modelName).Where(row => EF.Property<string>(row, "EID") == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(row => EF.Property<string?>(row, EntityOwnershipFields.OwnerId), (string?)after[EntityOwnershipFields.OwnerId])
            .SetProperty(row => EF.Property<string?>(row, EntityOwnershipFields.OwnerDepartmentId), (string?)after[EntityOwnershipFields.OwnerDepartmentId]), cancellationToken);
        await audit.RegisterBackfillAsync(resource, id, existing, after, cancellationToken);
        return new(1, 0);
    }
}

/// <summary>本次明确选择的真实结果；未解析对象仍留在待人工列表，不宣称已完成全库。</summary>
public sealed record EntityOwnershipBatchResult(int Changed, int Unresolved);

/// <summary>受控补齐边界的稳定业务失败，调用方按正式协议翻译。</summary>
public sealed class EntityOwnershipException(int statusCode, string code) : Exception(code)
{
    public int StatusCode => statusCode;
    public string Code => code;
}
