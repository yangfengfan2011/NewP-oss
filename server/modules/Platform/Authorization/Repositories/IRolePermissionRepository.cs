namespace ICP.Platform.Modules.Authorization.Repositories;

public interface IRolePermissionRepository
{
    Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default);
}
