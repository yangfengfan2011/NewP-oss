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
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "EID") == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<ConfigItem?> GetByKeyAsync(
        string key,
        string? environment = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // 配置命中顺序按“环境 + 租户最精确 -> 仅环境 -> 仅租户 -> 全局默认”回退，
        // 这样调用方只查一次就能得到当前上下文的最优生效项。
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
        // 模块级查询返回“当前上下文可能可见”的候选项，包含全局项和当前环境/租户项；
        // 具体冲突如何决议由上层服务决定，而不是在仓储层静默覆盖。
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
        // 先去重 keys，避免上层批量查询时把重复 key 放大成无意义 SQL 条件。
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
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == item.Id, cancellationToken);

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
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        Entities.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("ConfigItem");

    // 这里仍然使用字典实体而不是强类型 EF 实体，目的是复用平台建模层的统一存储表示，
    // 避免模块仓储绕开建模约束直接绑定数据库实现细节。
    private static ConfigItem Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        Key = GetString(entity, "Key")!,
        Value = GetString(entity, "Value"),
        Type = GetString(entity, "Type")!,
        ModuleId = GetString(entity, "ModuleId")!,
        Group = GetString(entity, "Group"),
        Environment = GetString(entity, "Environment"),
        TenantId = GetString(entity, "TenantId"),
        DisplayName = GetString(entity, "ENAME") ?? GetString(entity, "DisplayName")!,
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
            // ConfigItem 新模型由 metadata projector 生成后，底层主键字段名回到平台内核约定的 EID；
            // 仓储层继续对外暴露领域对象的 Id，避免上层服务感知到底层建模字段细节。
            ["EID"] = item.Id
        };

        CopyToEntity(item, entity);
        return entity;
    }

    private static void CopyToEntity(ConfigItem item, IDictionary<string, object?> entity)
    {
        // 更新时显式回写全部受控字段，保证仓储输出和建模层字段集合保持一致。
        entity["EID"] = item.Id;
        entity["ENAME"] = item.DisplayName;
        entity["Key"] = item.Key;
        entity["Value"] = item.Value;
        entity["Type"] = item.Type;
        entity["ModuleId"] = item.ModuleId;
        entity["Group"] = item.Group;
        entity["Environment"] = item.Environment;
        entity["TenantId"] = item.TenantId;
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
