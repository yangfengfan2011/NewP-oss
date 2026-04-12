using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IDepartmentRepository
{
    Task<List<Department>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<Department?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsSiblingCodeAsync(
        Guid companyId,
        Guid? parentDepartmentId,
        string code,
        Guid? excludedDepartmentId = null,
        CancellationToken cancellationToken = default);
    Task CreateAsync(Department department, CancellationToken cancellationToken = default);
    Task UpdateAsync(Department department, CancellationToken cancellationToken = default);
}
