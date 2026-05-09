using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface ICompanyRepository
{
    Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task CreateAsync(Company company, CancellationToken cancellationToken = default);
    Task UpdateAsync(Company company, CancellationToken cancellationToken = default);
}
