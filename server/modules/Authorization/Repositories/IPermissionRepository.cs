using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

public interface IPermissionRepository
{
    Task<List<Permission>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Permission?> GetByIdAsync(string permissionId, CancellationToken cancellationToken = default);
    Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetByIdsAsync(IEnumerable<string> permissionIds, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
    Task CreateAsync(Permission permission, CancellationToken cancellationToken = default);
    Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default);
}
