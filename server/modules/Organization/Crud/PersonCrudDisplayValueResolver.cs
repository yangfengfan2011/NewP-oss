using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDisplayValueResolver : IStandardCrudDisplayValueResolver
{
    private readonly ICompanyDirectory _companyDirectory;
    private readonly IDepartmentDirectory _departmentDirectory;
    private readonly IPositionDirectory _positionDirectory;

    /// <summary>
    /// 绑定组织归属窄目录与独立岗位目录，供一页人员显示值批量投影。
    /// </summary>
    public PersonCrudDisplayValueResolver(
        ICompanyDirectory companyDirectory,
        IDepartmentDirectory departmentDirectory,
        IPositionDirectory positionDirectory)
    {
        _companyDirectory = companyDirectory;
        _departmentDirectory = departmentDirectory;
        _positionDirectory = positionDirectory;
    }

    public string ResourceKey => "organization.person";

    /// <summary>
    /// 按请求字段批量补齐人员显示名；只批量读取当前真正需要的引用实体，避免列表逐行放大查询。
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        var requested = requestedDisplayFields.Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, Company> companies = requested.Contains("CompanyId_DisplayValue")
            ? await _companyDirectory.GetByIdsAsync(CollectIds(rows, "CompanyId"), cancellationToken)
            : new Dictionary<string, Company>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, Department> departments = requested.Contains("DepartmentName")
            ? await _departmentDirectory.GetByIdsAsync(CollectIds(rows, "DepartmentId"), cancellationToken)
            : new Dictionary<string, Department>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, Position> positions = requested.Contains("PositionName")
            ? await _positionDirectory.GetByIdsAsync(CollectIds(rows, "PositionId"), cancellationToken)
            : new Dictionary<string, Position>(StringComparer.Ordinal);

        return rows.Select(row =>
        {
            var displayValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (requested.Contains("CompanyId_DisplayValue"))
            {
                displayValues["CompanyId_DisplayValue"] = ReadDisplayName(row, "CompanyId", companies);
            }

            if (requested.Contains("DepartmentName"))
            {
                displayValues["DepartmentName"] = ReadDisplayName(row, "DepartmentId", departments);
            }

            if (requested.Contains("PositionName"))
            {
                displayValues["PositionName"] = ReadDisplayName(row, "PositionId", positions);
            }

            return (IReadOnlyDictionary<string, object?>)displayValues;
        }).ToList();
    }

    private static IReadOnlyCollection<string> CollectIds(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        string field) =>
        rows.Select(row => ReadStringOrNull(row, field))
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // 公司显示值保留当前名称，并显式标记停用事实。
    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Company> companies) =>
        ReadStringOrNull(row, sourceField) is { } id && companies.TryGetValue(id, out var company)
            ? company.IsEnabled ? company.Name : $"{company.Name}（已停用）"
            : string.Empty;

    // 部门显示值保留当前名称，并显式标记停用事实。
    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Department> departments) =>
        ReadStringOrNull(row, sourceField) is { } id && departments.TryGetValue(id, out var department)
            ? department.IsEnabled ? department.Name : $"{department.Name}（已停用）"
            : string.Empty;

    // 人员读取始终消费岗位目录当前名称，并在停用后保留可见的生命周期标记。
    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Position> positions) =>
        ReadStringOrNull(row, sourceField) is { } id && positions.TryGetValue(id, out var position)
            ? position.IsEnabled ? position.Name : $"{position.Name}（已停用）"
            : string.Empty;

    private static string? ReadStringOrNull(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
}
