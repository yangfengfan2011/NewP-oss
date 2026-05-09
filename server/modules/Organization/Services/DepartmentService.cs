using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using System.Globalization;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public DepartmentService(
        IDepartmentRepository departmentRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _departmentRepository = departmentRepository;
        _objectIdGenerator = objectIdGenerator;
    }

    public async Task<List<DepartmentDto>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var departments = await _departmentRepository.GetByCompanyIdAsync(companyId, cancellationToken);
        return departments.Select(MapToDto).ToList();
    }

    public async Task<DepartmentDto?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        return department is null ? null : MapToDto(department);
    }

    public async Task<DepartmentDto> CreateAsync(DepartmentDto dto, CancellationToken cancellationToken = default)
    {
        var companyId = RequireId(dto.CompanyId, nameof(dto.CompanyId));
        var parentDepartmentId = NormalizeOptionalId(dto.ParentDepartmentId);

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
            Id = _objectIdGenerator.NewId(nameof(Department)),
            CompanyId = companyId,
            ParentDepartmentId = parentDepartmentId,
            Code = dto.Code,
            Name = dto.DisplayName,
            SortOrder = ParseSortOrder(dto.SortOrder)
        };

        await _departmentRepository.CreateAsync(department, cancellationToken);
        return MapToDto(department);
    }

    public async Task<DepartmentDto> UpdateAsync(string departmentId, DepartmentDto dto, CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
        if (department is null)
        {
            throw new InvalidOperationException($"部门 '{departmentId}' 不存在。");
        }

        var companyId = RequireId(dto.CompanyId, nameof(dto.CompanyId));
        var parentDepartmentId = NormalizeOptionalId(dto.ParentDepartmentId);

        if (string.Equals(parentDepartmentId, departmentId, StringComparison.Ordinal))
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
        Id = department.Id,
        CompanyId = department.CompanyId,
        ParentDepartmentId = department.ParentDepartmentId,
        Code = department.Code,
        DisplayName = department.Name,
        IsEnabled = department.IsEnabled,
        SortOrder = department.SortOrder.ToString(CultureInfo.InvariantCulture),
        CreatedAt = department.CreatedAt,
        UpdatedAt = department.UpdatedAt
    };

    // 平台对象引用已是协议字符串；这里只拦截空值，避免服务层继续暗含 Guid 解析边界。
    private static string RequireId(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{propertyName} 不能为空。")
            : value;

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ParseSortOrder(string? sortOrder) =>
        string.IsNullOrWhiteSpace(sortOrder)
            ? 0
            : int.TryParse(sortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("SortOrder 必须是有效整数。");
}
