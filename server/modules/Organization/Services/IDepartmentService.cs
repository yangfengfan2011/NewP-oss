using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface IDepartmentService
{
    Task<List<DepartmentDto>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(Guid departmentId, UpdateDepartmentRequest request, CancellationToken cancellationToken = default);
}
