using ICP.Platform.Modules.Authorization.Models;

namespace ICP.Platform.Modules.Authorization.Repositories;

/// <summary>
/// 内存实现的权限仓储（待替换为真实数据库实现）
/// </summary>
public class PermissionRepository : IPermissionRepository
{
    private readonly List<Permission> _permissions = new();
    private readonly object _lock = new();

    public Task<List<Permission>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_permissions.ToList());
        }
    }

    public Task<Permission?> GetByIdAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_permissions.FirstOrDefault(p => p.Id == permissionId));
        }
    }

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_permissions.FirstOrDefault(p => p.Code == code));
        }
    }

    public Task<List<Permission>> GetByIdsAsync(IEnumerable<string> permissionIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idSet = permissionIds.ToHashSet();
            return Task.FromResult(_permissions.Where(p => idSet.Contains(p.Id)).ToList());
        }
    }

    public Task<List<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var codeSet = codes.ToHashSet();
            return Task.FromResult(_permissions.Where(p => codeSet.Contains(p.Code)).ToList());
        }
    }

    public Task CreateAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _permissions.Add(permission);
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _permissions.FindIndex(p => p.Id == permission.Id);
            if (index >= 0)
            {
                _permissions[index] = permission;
            }
            return Task.CompletedTask;
        }
    }
}
