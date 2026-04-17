using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

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
        if (await _departmentRepository.ExistsSiblingCodeAsync(
                dto.CompanyId,
                dto.ParentDepartmentId,
                dto.Code,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException("同一父节点下部门编码必须唯一。");
        }

        var department = new Department
        {
            CompanyId = dto.CompanyId,
            ParentDepartmentId = dto.ParentDepartmentId,
            Code = dto.Code,
            Name = dto.Name,
            SortOrder = dto.SortOrder
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

        if (dto.ParentDepartmentId == departmentId)
        {
            throw new InvalidOperationException("部门不能指向自己作为上级。");
        }

        if (await _departmentRepository.ExistsSiblingCodeAsync(
                dto.CompanyId,
                dto.ParentDepartmentId,
                department.Code,
                departmentId,
                cancellationToken))
        {
            throw new InvalidOperationException("同一父节点下部门编码必须唯一。");
        }

        department.CompanyId = dto.CompanyId;
        department.ParentDepartmentId = dto.ParentDepartmentId;
        department.Name = dto.Name;
        department.IsEnabled = dto.IsEnabled;
        department.SortOrder = dto.SortOrder;
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
        Name = department.Name,
        IsEnabled = department.IsEnabled,
        SortOrder = department.SortOrder,
        CreatedAt = department.CreatedAt
    };
}
