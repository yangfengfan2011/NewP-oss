using ICP.Platform.Modules.Organization.DTOs;
namespace ICP.Platform.Modules.Organization.Services;

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(DepartmentDto dto, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(string departmentId, DepartmentDto dto, CancellationToken cancellationToken = default);
}
