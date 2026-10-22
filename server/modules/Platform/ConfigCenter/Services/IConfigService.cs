using ICP.Platform.Modules.ConfigCenter.DTOs;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public interface IConfigService
{
    Task<List<ConfigItemDto>> GetAllConfigsAsync(string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<ConfigItemDto?> GetConfigAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<List<ConfigItemDto>> GetConfigsByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<List<ConfigGroupDto>> GetConfigsGroupedAsync(string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<ConfigItemDto> CreateConfigAsync(ConfigItemDto dto, string? createdBy = null, CancellationToken cancellationToken = default);
    Task<ConfigItemDto> UpdateConfigAsync(string id, ConfigItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task DeleteConfigAsync(string id, CancellationToken cancellationToken = default);
}
