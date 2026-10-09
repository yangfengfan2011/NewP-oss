using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>在同一组织结果中批量解析请求字段，保留原始值及既有 canonical companion 约束。</summary>
public sealed class DepartmentCrudDisplayValueResolver(IOrganizationQuery query, IDataScopeReadableIds readableIds) : IStandardCrudDisplayValueResolver
{
    public string ResourceKey => "organization.department";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        var requested = requestedDisplayFields.Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        var mappings = new (string Source, string Display, OrganizationRecordKind Kind)[]
        {
            ("CompanyId", "CompanyId_DisplayValue", OrganizationRecordKind.Company),
            ("ParentDepartmentId", "ParentDepartmentId_DisplayValue", OrganizationRecordKind.Department)
        }.Where(mapping => requested.Contains(mapping.Display)).ToArray();
        var result = rows.Select(_ => new Dictionary<string, object?>(StringComparer.Ordinal)).ToArray();
        if (mappings.Length == 0 || rows.Count == 0) return result;
        var view = await query.ReadAsync(cancellationToken);
        foreach (var (source, display, kind) in mappings)
        {
            var ids = rows.Select(row => row.GetValueOrDefault(source) as string)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
            var names = view.ResolveNames(kind, ids);
            var readable = await readableIds.GetReadableIdsAsync(OrganizationDisplayReadResources.For(kind), ids, cancellationToken);
            for (var index = 0; index < rows.Count; index++)
            {
                var id = rows[index].GetValueOrDefault(source) as string;
                if (id is null || !names.TryGetValue(id, out var name))
                {
                    result[index][display] = string.Empty;
                    continue;
                }
                result[index][display] = readable.Contains(id)
                    ? name.IsEnabled ? name.Name : $"{name.Name}（已停用）"
                    : "***";
            }
        }
        return result;
    }
}
