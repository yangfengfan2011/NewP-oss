using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDisplayValueResolver : IStandardCrudDisplayValueResolver
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;

    public PersonCrudDisplayValueResolver(
        ICompanyRepository companyRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository)
    {
        _companyRepository = companyRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
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
        var companyIds = requested.Contains("CompanyName") ? CollectIds(rows, "CompanyId") : [];
        var departmentIds = requested.Contains("DepartmentName") ? CollectIds(rows, "DepartmentId") : [];
        var positionIds = requested.Contains("PositionName") ? CollectIds(rows, "PositionId") : [];

        var companies = await _companyRepository.GetByIdsAsync(companyIds, cancellationToken);
        var departments = await _departmentRepository.GetByIdsAsync(departmentIds, cancellationToken);
        var positions = await _positionRepository.GetByIdsAsync(positionIds, cancellationToken);

        return rows.Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CompanyName"] = ReadDisplayName(row, "CompanyId", companies),
            ["DepartmentName"] = ReadDisplayName(row, "DepartmentId", departments),
            ["PositionName"] = ReadDisplayName(row, "PositionId", positions)
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

    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Company> companies) =>
        ReadStringOrNull(row, sourceField) is { } id && companies.TryGetValue(id, out var company)
            ? company.Name
            : string.Empty;

    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Department> departments) =>
        ReadStringOrNull(row, sourceField) is { } id && departments.TryGetValue(id, out var department)
            ? department.Name
            : string.Empty;

    private static string ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        string sourceField,
        IReadOnlyDictionary<string, Position> positions) =>
        ReadStringOrNull(row, sourceField) is { } id && positions.TryGetValue(id, out var position)
            ? position.Name
            : string.Empty;

    private static string? ReadStringOrNull(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
}
