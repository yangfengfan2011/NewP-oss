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
            .OrderBy(item => EF.Property<string>(item, "Code"))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<ValueListItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "Id") == id, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<ValueListItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, "Code") == code, cancellationToken);

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

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("ValueListItem");

    private static ValueListItem Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "Id")!,
        Code = GetString(entity, "Code")!,
        Name = GetString(entity, "Name")!,
        Description = GetString(entity, "Description"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy")
    };

    private static Dictionary<string, object?> ToEntity(ValueListItem item)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = item.Id
        };

        CopyToEntity(item, entity);
        return entity;
    }

    private static void CopyToEntity(ValueListItem item, IDictionary<string, object?> entity)
    {
        entity["Id"] = item.Id;
        entity["Code"] = item.Code;
        entity["Name"] = item.Name;
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
