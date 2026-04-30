using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using System.Globalization;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _departmentRepository;

    public DepartmentService(IDepartmentRepository departmentRepository)
    {
        _departmentRepository = departmentRepository;
    }

    public async Task<List<DepartmentDto>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var departments = await _departmentRepository.GetByCompanyIdAsync(companyId, cancellationToken);
        return departments.Select(MapToDto).ToList();
    }

    public async Task<DepartmentDto?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        return department is null ? null : MapToDto(department);
    }

    public async Task<DepartmentDto> CreateAsync(DepartmentDto dto, CancellationToken cancellationToken = default)
    {
        var companyId = ParseGuid(dto.CompanyId, nameof(dto.CompanyId));
        var parentDepartmentId = ParseNullableGuid(dto.ParentDepartmentId, nameof(dto.ParentDepartmentId));

        if (await _departmentRepository.ExistsSiblingCodeAsync(
                companyId,
                parentDepartmentId,
                dto.Code,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException("同一父节点下部门编码必须唯一。");
        }

        var department = new Department
        {
            CompanyId = companyId,
            ParentDepartmentId = parentDepartmentId,
            Code = dto.Code,
            Name = dto.DisplayName,
            SortOrder = ParseSortOrder(dto.SortOrder)
        };

        await _departmentRepository.CreateAsync(department, cancellationToken);
        return MapToDto(department);
    }

    public async Task<DepartmentDto> UpdateAsync(Guid departmentId, DepartmentDto dto, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department is null)
        {
            throw new InvalidOperationException($"部门 '{departmentId}' 不存在。");
        }

        var companyId = ParseGuid(dto.CompanyId, nameof(dto.CompanyId));
        var parentDepartmentId = ParseNullableGuid(dto.ParentDepartmentId, nameof(dto.ParentDepartmentId));

        if (parentDepartmentId == departmentId)
        {
            throw new InvalidOperationException("部门不能指向自己作为上级。");
        }

        if (await _departmentRepository.ExistsSiblingCodeAsync(
                companyId,
                parentDepartmentId,
                department.Code,
                departmentId,
                cancellationToken))
        {
            throw new InvalidOperationException("同一父节点下部门编码必须唯一。");
        }

        department.CompanyId = companyId;
        department.ParentDepartmentId = parentDepartmentId;
        department.Name = dto.DisplayName;
        department.IsEnabled = dto.IsEnabled;
        department.SortOrder = ParseSortOrder(dto.SortOrder);
        department.UpdatedAt = DateTime.UtcNow;

        await _departmentRepository.UpdateAsync(department, cancellationToken);
        return MapToDto(department);
    }

    private static DepartmentDto MapToDto(Department department) => new()
    {
        Id = department.Id.ToString("D"),
        CompanyId = department.CompanyId.ToString("D"),
        ParentDepartmentId = department.ParentDepartmentId?.ToString("D"),
        Code = department.Code,
        DisplayName = department.Name,
        IsEnabled = department.IsEnabled,
        SortOrder = department.SortOrder.ToString(CultureInfo.InvariantCulture),
        CreatedAt = department.CreatedAt,
        UpdatedAt = department.UpdatedAt
    };

    // 生成 DTO 的标识字段由 catalog 输出为 string；服务层在进入领域前集中校验失败信号。
    private static Guid ParseGuid(string value, string propertyName) =>
        Guid.TryParse(value, out var guid)
            ? guid
            : throw new InvalidOperationException($"{propertyName} 必须是有效 GUID。");

    private static Guid? ParseNullableGuid(string? value, string propertyName) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseGuid(value, propertyName);

    private static int ParseSortOrder(string? sortOrder) =>
        string.IsNullOrWhiteSpace(sortOrder)
            ? 0
            : int.TryParse(sortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("SortOrder 必须是有效整数。");
}
