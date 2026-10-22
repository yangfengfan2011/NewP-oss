using ICP.Platform.Modules.ConfigCenter.Models;

namespace ICP.Platform.Modules.ConfigCenter.Repositories;

public interface IConfigRepository
{
    Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ConfigItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ConfigItem?> GetByKeyAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<List<ConfigItem>> GetByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<List<ConfigItem>> GetByKeysAsync(IEnumerable<string> keys, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(ConfigItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
