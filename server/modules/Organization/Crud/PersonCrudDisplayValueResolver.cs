using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Repositories;

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
    /// 为标准 CRUD 行数据补齐展示名；引用读取失败时返回空字符串，由调用侧继续展示原始字段。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken = default)
    {
        var companyId = ReadStringOrNull(row, "CompanyId");
        var departmentId = ReadStringOrNull(row, "DepartmentId");
        var positionId = ReadStringOrNull(row, "PositionId");

        var company = companyId is not null ? await _companyRepository.GetByIdAsync(companyId, cancellationToken) : null;
        var department = departmentId is not null ? await _departmentRepository.GetByIdAsync(departmentId, cancellationToken) : null;
        var position = positionId is not null ? await _positionRepository.GetByIdAsync(positionId, cancellationToken) : null;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CompanyName"] = company?.Name ?? string.Empty,
            ["DepartmentName"] = department?.Name ?? string.Empty,
            ["PositionName"] = position?.Name ?? string.Empty
        };
    }

    private static string? ReadStringOrNull(IReadOnlyDictionary<string, object?> row, string field) =>
        row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
}
