using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(DepartmentDto dto, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(Guid departmentId, DepartmentDto dto, CancellationToken cancellationToken = default);
}
