using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public class ConfigService : IConfigService
{
    private readonly IConfigRepository _configRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public ConfigService(
        IConfigRepository configRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _configRepository = configRepository;
        _objectIdGenerator = objectIdGenerator;
    }

    public async Task<List<ConfigItemDto>> GetAllConfigsAsync(string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var items = await _configRepository.GetAllAsync(cancellationToken);

        // 过滤环境和租户
        if (environment != null || tenantId != null)
        {
            items = items.Where(i =>
                (environment == null || i.Environment == null || i.Environment == environment) &&
                (tenantId == null || i.TenantId == null || i.TenantId == tenantId))
                .ToList();
        }

        return items.Select(MapToDto).ToList();
    }

    public async Task<ConfigItemDto?> GetConfigAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var item = await _configRepository.GetByKeyAsync(key, environment, tenantId, cancellationToken);
        return item != null ? MapToDto(item) : null;
    }

    public async Task<List<ConfigItemDto>> GetConfigsByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var items = await _configRepository.GetByModuleAsync(moduleId, environment, tenantId, cancellationToken);
        return items.Select(MapToDto).ToList();
    }

    public async Task<List<ConfigGroupDto>> GetConfigsGroupedAsync(string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var items = await GetAllConfigsAsync(environment, tenantId, cancellationToken);

        var grouped = items
            .GroupBy(i => new { i.ModuleId, i.Group })
            .Select(g => new ConfigGroupDto
            {
                ModuleId = g.Key.ModuleId,
                Group = g.Key.Group,
                Items = g.ToList()
            })
            .ToList();

        return grouped;
    }

    public async Task<ConfigItemDto> CreateConfigAsync(ConfigItemDto dto, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        var item = new ConfigItem
        {
            Id = _objectIdGenerator.NewId(nameof(ConfigItem)),
            Key = dto.Key,
            Value = dto.Value,
            Type = dto.Type,
            ModuleId = dto.ModuleId,
            Group = dto.Group,
            Environment = dto.Environment,
            TenantId = dto.TenantId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            IsSensitive = dto.IsSensitive,
            UpdatedBy = createdBy
        };

        await _configRepository.CreateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task<ConfigItemDto> UpdateConfigAsync(string id, ConfigItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        var item = await _configRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            throw new InvalidOperationException($"配置项 {id} 不存在");
        }

        // 同一 DTO 只负责统一输入/输出模型，不改变当前 update 只允许编辑的字段范围。
        if (dto.Value != null)
        {
            item.Value = dto.Value;
        }

        if (dto.DisplayName != null)
        {
            item.DisplayName = dto.DisplayName;
        }

        if (dto.Description != null)
        {
            item.Description = dto.Description;
        }

        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = updatedBy;

        await _configRepository.UpdateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task DeleteConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        await _configRepository.DeleteAsync(id, cancellationToken);
    }

    private static ConfigItemDto MapToDto(ConfigItem item)
    {
        return new ConfigItemDto
        {
            Id = item.Id,
            Key = item.Key,
            Value = item.IsSensitive ? "******" : item.Value,
            Type = item.Type,
            ModuleId = item.ModuleId,
            Group = item.Group,
            Environment = item.Environment,
            TenantId = item.TenantId,
            DisplayName = item.DisplayName,
            Description = item.Description,
            IsSensitive = item.IsSensitive,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy
        };
    }
}
