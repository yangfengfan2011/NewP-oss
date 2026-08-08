using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Ef;

public sealed class EfValueListRepository : IValueListRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfValueListRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ValueListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .OrderBy(item => EF.Property<string>(item, "ECODE"))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<ValueListItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "EID") == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<ValueListItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "ECODE") == code, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task CreateAsync(ValueListItem item, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(item));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ValueListItem item, CancellationToken cancellationToken = default)
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

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("ValueListItem"));

    private static ValueListItem Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        Code = GetString(entity, "ECODE")!,
        Name = GetString(entity, "ENAME")!,
        Description = GetString(entity, "Description"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy")
    };

    private static Dictionary<string, object?> ToEntity(ValueListItem item)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = item.Id
        };

        CopyToEntity(item, entity);
        return entity;
    }

    private static void CopyToEntity(ValueListItem item, IDictionary<string, object?> entity)
    {
        // 领域对象仍暴露 Id；metadata 投影后的实体主键统一写入 EID。
        entity["EID"] = item.Id;
        entity["ECODE"] = item.Code;
        entity["ENAME"] = item.Name;
        entity["Description"] = item.Description;
        entity["CreatedAt"] = item.CreatedAt;
        entity["UpdatedAt"] = item.UpdatedAt;
        entity["UpdatedBy"] = item.UpdatedBy;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;
}
