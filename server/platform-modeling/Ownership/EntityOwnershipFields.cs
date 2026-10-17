using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modeling.Ownership;

/// <summary>ENTITY 按需选用的归属字段；归属是当前业务事实，不属于不可改写的创建审计。</summary>
public static class EntityOwnershipFields
{
    public const string OwnerId = "OwnerId";
    public const string OwnerDepartmentId = "OwnerDepartmentId";

    /// <summary>判断字段是否属于平台统一定义的 ENTITY 归属字段。</summary>
    public static bool Contains(string field) => string.Equals(field, OwnerId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(field, OwnerDepartmentId, StringComparison.OrdinalIgnoreCase);

    /// <summary>只为已选用字段投影可空存储，旧行留待显式补齐，不制造历史归属。</summary>
    public static IEnumerable<FieldDefinition> Project(IReadOnlyCollection<string> selected)
    {
        if (selected.Contains(OwnerId, StringComparer.OrdinalIgnoreCase))
            yield return new(OwnerId, OwnerId, FieldKind.Reference, FieldValueType.User, "string", false, false, false,
                new("user", "IdentityAccount", "EID", "ENAME")) { IsOwnershipField = true };
        if (selected.Contains(OwnerDepartmentId, StringComparer.OrdinalIgnoreCase))
            yield return new(OwnerDepartmentId, OwnerDepartmentId, FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false,
                new("reference", "Department", "EID", "ENAME")) { IsOwnershipField = true };
    }
}
