using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudDisplayValueResolver(ICompanyDirectory companyDirectory)
    : IStandardCrudDisplayValueResolver
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 一次批量读取当前页涉及的公司，并补齐部门列表的 canonical CompanyId display value。
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        if (!requestedDisplayFields.Any(static field => field.Field == "CompanyId_DisplayValue"))
        {
            return rows.Select(static _ => (IReadOnlyDictionary<string, object?>)
                new Dictionary<string, object?>(StringComparer.Ordinal)).ToList();
        }

        var ids = rows
            .Select(static row => row.GetValueOrDefault("CompanyId")?.ToString())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var companies = await companyDirectory.GetByIdsAsync(ids, cancellationToken);
        return rows.Select(row =>
        {
            var companyId = row.GetValueOrDefault("CompanyId")?.ToString();
            var displayValue = companyId is not null && companies.TryGetValue(companyId, out var company)
                ? company.IsEnabled ? company.Name : $"{company.Name}（已停用）"
                : string.Empty;
            return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CompanyId_DisplayValue"] = displayValue
            };
        }).ToList();
    }
}
