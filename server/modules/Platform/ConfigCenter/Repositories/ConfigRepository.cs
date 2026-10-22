using ICP.Platform.Modules.ConfigCenter.Models;

namespace ICP.Platform.Modules.ConfigCenter.Repositories;

/// <summary>
/// 内存实现的配置仓储（待替换为真实数据库实现）
/// </summary>
public class ConfigRepository : IConfigRepository
{
    private readonly List<ConfigItem> _items = new();
    private readonly object _lock = new();

    public Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.ToList());
        }
    }

    public Task<ConfigItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        }
    }

    public Task<ConfigItem?> GetByKeyAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            // 优先级：租户+环境 > 环境 > 租户 > 全局
            var item = _items.FirstOrDefault(i =>
                i.Key == key &&
                i.Environment == environment &&
                i.TenantId == tenantId);

            if (item == null && environment != null)
            {
                item = _items.FirstOrDefault(i =>
                    i.Key == key &&
                    i.Environment == environment &&
                    i.TenantId == null);
            }

            if (item == null && tenantId != null)
            {
                item = _items.FirstOrDefault(i =>
                    i.Key == key &&
                    i.Environment == null &&
                    i.TenantId == tenantId);
            }

            if (item == null)
            {
                item = _items.FirstOrDefault(i =>
                    i.Key == key &&
                    i.Environment == null &&
                    i.TenantId == null);
            }

            return Task.FromResult(item);
        }
    }

    public Task<List<ConfigItem>> GetByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var items = _items.Where(i =>
                i.ModuleId == moduleId &&
                (environment == null || i.Environment == null || i.Environment == environment) &&
                (tenantId == null || i.TenantId == null || i.TenantId == tenantId))
                .ToList();

            return Task.FromResult(items);
        }
    }

    public Task<List<ConfigItem>> GetByKeysAsync(IEnumerable<string> keys, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var keySet = keys.ToHashSet();
            var items = _items.Where(i =>
                keySet.Contains(i.Key) &&
                (environment == null || i.Environment == null || i.Environment == environment) &&
                (tenantId == null || i.TenantId == null || i.TenantId == tenantId))
                .ToList();

            return Task.FromResult(items);
        }
    }

    public Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _items.Add(item);
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == item.Id);
            if (index >= 0)
            {
                _items[index] = item;
            }
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
            return Task.CompletedTask;
        }
    }
}
