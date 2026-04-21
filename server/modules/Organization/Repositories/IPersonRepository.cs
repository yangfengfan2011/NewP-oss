using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IPersonRepository
{
    Task<Person?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Person>> GetListAsync(
        string? keyword,
        Guid? companyId,
        Guid? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsPersonNoAsync(
        Guid? companyId,
        string personNo,
        Guid? excludedPersonId = null,
        CancellationToken cancellationToken = default);
    Task CreateAsync(Person person, CancellationToken cancellationToken = default);
    Task UpdateAsync(Person person, CancellationToken cancellationToken = default);
}
