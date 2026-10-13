using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

public interface IUserRoleRepository
{
    Task<List<UserRole>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task CreateBatchAsync(IEnumerable<UserRole> userRoles, CancellationToken cancellationToken = default);
    Task DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default);
}
