using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Ownership;

/// <summary>已选用归属字段的共享安全显示，账号与部门分别由真实目录批量裁决。</summary>
public sealed class EntityOwnershipDisplayResolver(string resourceKey, IUserDisplayNameDirectory users,
    IAccountOwnershipDirectory departments) : IStandardCrudDisplayValueResolver
{
    public string ResourceKey => resourceKey;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        var owners = rows.Select(row => row.GetValueOrDefault("OwnerId")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var departmentIds = rows.Select(row => row.GetValueOrDefault("OwnerDepartmentId")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var userNames = await users.FindManyAsync(owners, cancellationToken);
        var departmentNames = await departments.FindDepartmentNamesAsync(departmentIds, cancellationToken);
        return rows.Select(row => (IReadOnlyDictionary<string, object?>)requestedDisplayFields.ToDictionary(field => field.Field,
            field => field.Field switch
            {
                "OwnerId_DisplayValue" => row.GetValueOrDefault("OwnerId") is string id ? (object?)userNames.GetValueOrDefault(id) : null,
                "OwnerDepartmentId_DisplayValue" => row.GetValueOrDefault("OwnerDepartmentId") is string id ? departmentNames.GetValueOrDefault(id) : null,
                _ => throw new InvalidOperationException("ENTITY_OWNERSHIP_DISPLAY_FIELD_INVALID")
            }, StringComparer.Ordinal)).ToArray();
    }
}
