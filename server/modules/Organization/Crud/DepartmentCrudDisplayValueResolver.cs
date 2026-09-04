using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudDisplayValueResolver(
    ICompanyDirectory companyDirectory,
    IDepartmentDirectory departmentDirectory)
    : IStandardCrudDisplayValueResolver
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 各用一次批量读取补齐当前页的 canonical 公司与上级部门 display value。
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        var resolveCompany = requestedDisplayFields.Any(static field => field.Field == "CompanyId_DisplayValue");
        var resolveParent = requestedDisplayFields.Any(static field => field.Field == "ParentDepartmentId_DisplayValue");
        if (!resolveCompany && !resolveParent)
        {
            return rows.Select(static _ => (IReadOnlyDictionary<string, object?>)
                new Dictionary<string, object?>(StringComparer.Ordinal)).ToList();
        }

        var companyIds = resolveCompany ? ReadIds(rows, "CompanyId") : [];
        var parentIds = resolveParent ? ReadIds(rows, "ParentDepartmentId") : [];
        var companies = resolveCompany
            ? await companyDirectory.GetByIdsAsync(companyIds, cancellationToken)
            : new Dictionary<string, Company>(StringComparer.Ordinal);
        var parents = resolveParent
            ? await departmentDirectory.GetByIdsAsync(parentIds, cancellationToken)
            : new Dictionary<string, Department>(StringComparer.Ordinal);
        return rows.Select(row =>
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (resolveCompany)
            {
                values["CompanyId_DisplayValue"] = ReadDisplay(row, "CompanyId", companies);
            }

            if (resolveParent)
            {
                values["ParentDepartmentId_DisplayValue"] = ReadDisplay(row, "ParentDepartmentId", parents);
            }

            return (IReadOnlyDictionary<string, object?>)values;
        }).ToList();
    }

    // 当前页标识先去空白和重复，保证每个目录最多执行一次批量读取。
    private static string[] ReadIds(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string field) => rows
            .Select(row => row.GetValueOrDefault(field)?.ToString())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // 目录缺失时保留空 companion；停用事实只影响显示标记，不清除既有引用。
    private static string ReadDisplay<T>(
        IReadOnlyDictionary<string, object?> row,
        string field,
        IReadOnlyDictionary<string, T> directory) where T : class
    {
        var id = row.GetValueOrDefault(field)?.ToString();
        if (id is null || !directory.TryGetValue(id, out var value))
        {
            return string.Empty;
        }

        return value switch
        {
            Company company => company.IsEnabled ? company.Name : $"{company.Name}（已停用）",
            Department department => department.IsEnabled ? department.Name : $"{department.Name}（已停用）",
            _ => string.Empty
        };
    }
}
