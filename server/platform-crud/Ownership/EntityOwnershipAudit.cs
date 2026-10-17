using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Crud.Ownership;

/// <summary>声明归属初始化和受控补齐的真实结果，登记与对象写入共用原平台事务。</summary>
public sealed class EntityOwnershipAudit(IBusinessAuditRecorder? recorder = null)
{
    /// <summary>仅登记实际缺失字段的前后事实，不伪造旧部门或覆盖已存在的归属。</summary>
    public ValueTask RegisterBackfillAsync(string resource, string id, IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after, CancellationToken cancellationToken = default)
    {
        var fields = new[] { (Name: "OwnerId", Kind: "user"), (Name: "OwnerDepartmentId", Kind: "department") }
            .Where(field => !Equals(before.GetValueOrDefault(field.Name), after.GetValueOrDefault(field.Name)))
            .Select(field => new BusinessAuditField(field.Name, BusinessAuditValue.Reference(field.Kind, before.GetValueOrDefault(field.Name) as string),
                BusinessAuditValue.Reference(field.Kind, after.GetValueOrDefault(field.Name) as string))).ToArray();
        return fields.Length == 0 ? ValueTask.CompletedTask
            : (recorder ?? throw new InvalidOperationException("ENTITY_OWNERSHIP_AUDIT_REQUIRED")).RegisterAsync(new("entity.ownership.backfilled", resource, id, fields), cancellationToken);
    }
    /// <summary>只记录已建立的归属事实；调用方不得把旧值补齐声称为创建时历史。</summary>
    public ValueTask RegisterAsync(string resource, string id, IReadOnlyDictionary<string, object?> values,
        bool backfilled, CancellationToken cancellationToken = default) => (recorder ?? throw new InvalidOperationException("ENTITY_OWNERSHIP_AUDIT_REQUIRED")).RegisterAsync(new(
            backfilled ? "entity.ownership.backfilled" : "entity.ownership.initialized", resource, id,
            new[] { (Name: "OwnerId", Kind: "user"), (Name: "OwnerDepartmentId", Kind: "department") }
                .Where(field => values.ContainsKey(field.Name))
                .Select(field => new BusinessAuditField(field.Name, null, BusinessAuditValue.Reference(field.Kind, values[field.Name] as string))).ToArray()), cancellationToken);
}
