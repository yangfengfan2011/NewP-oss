using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IDepartmentRepository
{
    Task<List<Department>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
        IReadOnlyCollection<string> departmentIds,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsSiblingCodeAsync(
        string companyId,
        string? parentDepartmentId,
        string code,
        string? excludedDepartmentId = null,
        CancellationToken cancellationToken = default);
    Task CreateAsync(Department department, CancellationToken cancellationToken = default);
    Task UpdateAsync(Department department, CancellationToken cancellationToken = default);
}
