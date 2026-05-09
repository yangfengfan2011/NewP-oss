using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IPersonRepository
{
    Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Person>> GetListAsync(
        string? keyword,
        string? companyId,
        string? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsPersonNoAsync(
        string? companyId,
        string personNo,
        string? excludedPersonId = null,
        CancellationToken cancellationToken = default);
    Task CreateAsync(Person person, CancellationToken cancellationToken = default);
    Task UpdateAsync(Person person, CancellationToken cancellationToken = default);
}
