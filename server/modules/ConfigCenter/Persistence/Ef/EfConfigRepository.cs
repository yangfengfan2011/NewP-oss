using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Ef;

public sealed class EfConfigRepository : IConfigRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfConfigRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(Map).ToList();
    }

    public async Task<ConfigItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "Id") == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<ConfigItem?> GetByKeyAsync(
        string key,
        string? environment = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .Where(item => EF.Property<string>(item, "Key") == key)
            .OrderByDescending(item =>
                EF.Property<string?>(item, "Environment") == environment &&
                EF.Property<string?>(item, "TenantId") == tenantId)
            .ThenByDescending(item =>
                environment != null &&
                EF.Property<string?>(item, "Environment") == environment &&
                EF.Property<string?>(item, "TenantId") == null)
            .ThenByDescending(item =>
                tenantId != null &&
                EF.Property<string?>(item, "Environment") == null &&
                EF.Property<string?>(item, "TenantId") == tenantId)
            .ThenByDescending(item =>
                EF.Property<string?>(item, "Environment") == null &&
                EF.Property<string?>(item, "TenantId") == null)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<List<ConfigItem>> GetByModuleAsync(
        string moduleId,
        string? environment = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .Where(item =>
                EF.Property<string>(item, "ModuleId") == moduleId &&
                (environment == null ||
                 EF.Property<string?>(item, "Environment") == null ||
                 EF.Property<string?>(item, "Environment") == environment) &&
                (tenantId == null ||
                 EF.Property<string?>(item, "TenantId") == null ||
                 EF.Property<string?>(item, "TenantId") == tenantId))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<List<ConfigItem>> GetByKeysAsync(
        IEnumerable<string> keys,
        string? environment = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var entities = await Entities
            .AsNoTracking()
            .Where(item =>
                keyList.Contains(EF.Property<string>(item, "Key")) &&
                (environment == null ||
                 EF.Property<string?>(item, "Environment") == null ||
                 EF.Property<string?>(item, "Environment") == environment) &&
                (tenantId == null ||
                 EF.Property<string?>(item, "TenantId") == null ||
                 EF.Property<string?>(item, "TenantId") == tenantId))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(item));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "Id") == item.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(item, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "Id") == id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        Entities.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("ConfigItem");

    private static ConfigItem Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "Id")!,
        Key = GetString(entity, "Key")!,
        Value = GetString(entity, "Value"),
        Type = GetString(entity, "Type")!,
        ModuleId = GetString(entity, "ModuleId")!,
        Group = GetString(entity, "Group"),
        Environment = GetString(entity, "Environment"),
        TenantId = GetString(entity, "TenantId"),
        DisplayName = GetString(entity, "DisplayName")!,
        Description = GetString(entity, "Description"),
        IsSensitive = GetBool(entity, "IsSensitive"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy")
    };

    private static Dictionary<string, object?> ToEntity(ConfigItem item)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = item.Id
        };

        CopyToEntity(item, entity);
        return entity;
    }

    private static void CopyToEntity(ConfigItem item, IDictionary<string, object?> entity)
    {
        entity["Id"] = item.Id;
        entity["Key"] = item.Key;
        entity["Value"] = item.Value;
        entity["Type"] = item.Type;
        entity["ModuleId"] = item.ModuleId;
        entity["Group"] = item.Group;
        entity["Environment"] = item.Environment;
        entity["TenantId"] = item.TenantId;
        entity["DisplayName"] = item.DisplayName;
        entity["Description"] = item.Description;
        entity["IsSensitive"] = item.IsSensitive;
        entity["CreatedAt"] = item.CreatedAt;
        entity["UpdatedAt"] = item.UpdatedAt;
        entity["UpdatedBy"] = item.UpdatedBy;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;
}
