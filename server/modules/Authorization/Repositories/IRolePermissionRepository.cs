using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

public interface IRolePermissionRepository
{
    Task<List<RolePermission>> GetByRoleIdAsync(string roleId, CancellationToken cancellationToken = default);
    Task<List<RolePermission>> GetByRoleIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    Task CreateBatchAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken cancellationToken = default);
    Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default);
}
