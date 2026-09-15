using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

/// <summary>
/// 仅供既有角色删除消费者清理遗留关系的内存仓储。
/// </summary>
public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly List<RolePermission> _rolePermissions = new();
    private readonly object _lock = new();

    /// <summary>清理指定角色的遗留关系；不提供旧授权读取或写入入口。</summary>
    public Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rolePermissions.RemoveAll(rp => rp.RoleId == roleId);
            return Task.CompletedTask;
        }
    }
}
