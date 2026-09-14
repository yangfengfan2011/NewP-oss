using ICP.Platform.Modules.Authorization.Repositories;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>为认证主体投影当前启用角色，不承担功能位置决策。</summary>
public class AuthorizationService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository) : IAuthorizationService
{
    /// <inheritdoc/>
    public async Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetByUserIdAsync(userId, cancellationToken);
        if (!userRoles.Any())
        {
            return new List<string>();
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await roleRepository.GetByIdsAsync(roleIds, cancellationToken);

        return roles.Where(r => r.IsEnabled).Select(r => r.Code).ToList();
    }
}
