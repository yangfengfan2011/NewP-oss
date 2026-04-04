using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

/// <summary>
/// 内存实现的角色权限关系仓储（待替换为真实数据库实现）
/// </summary>
public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly List<RolePermission> _rolePermissions = new();
    private readonly object _lock = new();

    public Task<List<RolePermission>> GetByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rolePermissions.Where(rp => rp.RoleId == roleId).ToList());
        }
    }

    public Task<List<RolePermission>> GetByRoleIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idSet = roleIds.ToHashSet();
            return Task.FromResult(_rolePermissions.Where(rp => idSet.Contains(rp.RoleId)).ToList());
        }
    }

    public Task CreateBatchAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rolePermissions.AddRange(rolePermissions);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rolePermissions.RemoveAll(rp => rp.RoleId == roleId);
            return Task.CompletedTask;
        }
    }
}
