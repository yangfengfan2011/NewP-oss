using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

/// <summary>
/// 内存实现的角色仓储（待替换为真实数据库实现）
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly List<Role> _roles = new();
    private readonly object _lock = new();

    public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_roles.ToList());
        }
    }

    public Task<Role?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_roles.FirstOrDefault(r => r.Id == roleId));
        }
    }

    public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_roles.FirstOrDefault(r => r.Code == code));
        }
    }

    public Task<List<Role>> GetByIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idSet = roleIds.ToHashSet();
            return Task.FromResult(_roles.Where(r => idSet.Contains(r.Id)).ToList());
        }
    }

    public Task<List<Role>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var codeSet = codes.ToHashSet();
            return Task.FromResult(_roles.Where(r => codeSet.Contains(r.Code)).ToList());
        }
    }

    public Task CreateAsync(Role role, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _roles.Add(role);
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(Role role, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _roles.FindIndex(r => r.Id == role.Id);
            if (index >= 0)
            {
                _roles[index] = role;
            }
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string roleId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _roles.RemoveAll(r => r.Id == roleId);
            return Task.CompletedTask;
        }
    }
}
