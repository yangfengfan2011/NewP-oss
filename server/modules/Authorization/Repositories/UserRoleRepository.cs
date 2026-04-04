using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

/// <summary>
/// 内存实现的用户角色关系仓储（待替换为真实数据库实现）
/// </summary>
public class UserRoleRepository : IUserRoleRepository
{
    private readonly List<UserRole> _userRoles = new();
    private readonly object _lock = new();

    public Task<List<UserRole>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_userRoles.Where(ur => ur.UserId == userId).ToList());
        }
    }

    public Task CreateBatchAsync(IEnumerable<UserRole> userRoles, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _userRoles.AddRange(userRoles);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _userRoles.RemoveAll(ur => ur.UserId == userId);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _userRoles.RemoveAll(ur => ur.RoleId == roleId);
            return Task.CompletedTask;
        }
    }
}
