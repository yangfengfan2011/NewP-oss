using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class AuthorizationService : IAuthorizationService
{
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRoleRepository _roleRepository;

    public AuthorizationService(
        IUserRoleRepository userRoleRepository,
        IRolePermissionRepository rolePermissionRepository,
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository)
    {
        _userRoleRepository = userRoleRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _permissionRepository = permissionRepository;
        _roleRepository = roleRepository;
    }

    public async Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(permissionCode);
    }

    public async Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissionCodes.Any(code => userPermissions.Contains(code));
    }

    public async Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissionCodes.All(code => userPermissions.Contains(code));
    }

    public async Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        // 获取用户的所有角色
        var userRoles = await _userRoleRepository.GetByUserIdAsync(userId, cancellationToken);
        if (!userRoles.Any())
        {
            return new List<string>();
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        // 检查角色是否启用
        var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);
        var enabledRoleIds = roles.Where(r => r.IsEnabled).Select(r => r.Id).ToList();

        if (!enabledRoleIds.Any())
        {
            return new List<string>();
        }

        // 获取角色的所有权限
        var rolePermissions = await _rolePermissionRepository.GetByRoleIdsAsync(enabledRoleIds, cancellationToken);
        var permissionIds = rolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();

        if (!permissionIds.Any())
        {
            return new List<string>();
        }

        // 获取权限代码
        var permissions = await _permissionRepository.GetByIdsAsync(permissionIds, cancellationToken);
        return permissions.Select(p => p.Code).ToList();
    }

    public async Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await _userRoleRepository.GetByUserIdAsync(userId, cancellationToken);
        if (!userRoles.Any())
        {
            return new List<string>();
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);

        return roles.Where(r => r.IsEnabled).Select(r => r.Code).ToList();
    }
}
