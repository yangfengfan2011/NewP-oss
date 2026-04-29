using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence.Ef;

public sealed class EfRoleRepository : IRoleRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfRoleRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(Map).ToList();
    }

    public async Task<Role?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(role => EF.Property<string>(role, "EID") == roleId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(role => EF.Property<string>(role, "Code") == code, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<List<Role>> GetByIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await Entities
            .AsNoTracking()
            .Where(role => ids.Contains(EF.Property<string>(role, "EID")))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<List<Role>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var codeList = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await Entities
            .AsNoTracking()
            .Where(role => codeList.Contains(EF.Property<string>(role, "Code")))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task CreateAsync(Role role, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(role));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Role role, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == role.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(role, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == roleId, cancellationToken);

        if (entity is null)
        {
            return;
        }

        Entities.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("Role");

    private static Role Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        Code = GetString(entity, "Code")!,
        Name = GetString(entity, "Name")!,
        Description = GetString(entity, "Description"),
        IsEnabled = GetBool(entity, "IsEnabled"),
        IsBuiltIn = GetBool(entity, "IsBuiltIn"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        CreatedBy = GetString(entity, "CreatedBy"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy")
    };

    private static Dictionary<string, object?> ToEntity(Role role)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = role.Id
        };

        CopyToEntity(role, entity);
        return entity;
    }

    private static void CopyToEntity(Role role, IDictionary<string, object?> entity)
    {
        // 领域对象仍暴露 Id；持久化真源切换后主键字段固定为 EID。
        entity["EID"] = role.Id;
        entity["Code"] = role.Code;
        entity["Name"] = role.Name;
        entity["Description"] = role.Description;
        entity["IsEnabled"] = role.IsEnabled;
        entity["IsBuiltIn"] = role.IsBuiltIn;
        entity["CreatedAt"] = role.CreatedAt;
        entity["CreatedBy"] = role.CreatedBy;
        entity["UpdatedAt"] = role.UpdatedAt;
        entity["UpdatedBy"] = role.UpdatedBy;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : null;
}
