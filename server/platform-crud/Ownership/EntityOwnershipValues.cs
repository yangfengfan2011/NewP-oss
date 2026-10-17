using ICP.Platform.Modeling.Ownership;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Ownership;

namespace ICP.Platform.Crud.Ownership;

/// <summary>在候选判权前初始化已选用的归属字段；普通更新不调用，保留独立归属事实。</summary>
public sealed class EntityOwnershipValues(IEntityAuditValueProvider audit, IAccountOwnershipDirectory? directory = null)
{
    /// <summary>按可信操作账号的当前部门填充创建归属；无法解析时失败，绝不默认管理员。</summary>
    public async ValueTask ApplyCreateAsync(IDictionary<string, object?> values, IEnumerable<string> selectedFields,
        CancellationToken cancellationToken = default)
    {
        var fields = selectedFields.Where(EntityOwnershipFields.Contains).ToHashSet(StringComparer.Ordinal);
        if (fields.Count == 0) return;
        var actor = audit.Current().ActorId;
        var accounts = await (directory ?? throw new InvalidOperationException("ENTITY_OWNERSHIP_PROVIDER_REQUIRED")).FindManyAsync([actor], cancellationToken);
        if (!accounts.TryGetValue(actor, out var account)
            || fields.Contains(EntityOwnershipFields.OwnerDepartmentId) && string.IsNullOrWhiteSpace(account.DepartmentId))
            throw new InvalidOperationException("ENTITY_OWNERSHIP_UNAVAILABLE");
        if (fields.Contains(EntityOwnershipFields.OwnerId)) values[EntityOwnershipFields.OwnerId] = account.AccountId;
        if (fields.Contains(EntityOwnershipFields.OwnerDepartmentId)) values[EntityOwnershipFields.OwnerDepartmentId] = account.DepartmentId;
    }
}
