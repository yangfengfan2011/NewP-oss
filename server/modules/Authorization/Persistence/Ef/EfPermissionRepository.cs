using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence.Ef;

public sealed class EfPermissionRepository : IPermissionRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPermissionRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Permission>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(Map).ToList();
    }

    public async Task<Permission?> GetByIdAsync(string permissionId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(permission => EF.Property<string>(permission, "EID") == permissionId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(permission => EF.Property<string>(permission, "ECODE") == code, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<List<Permission>> GetByIdsAsync(IEnumerable<string> permissionIds, CancellationToken cancellationToken = default)
    {
        var ids = permissionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await Entities
            .AsNoTracking()
            .Where(permission => ids.Contains(EF.Property<string>(permission, "EID")))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<List<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var codeList = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await Entities
            .AsNoTracking()
            .Where(permission => codeList.Contains(EF.Property<string>(permission, "ECODE")))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task CreateAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(permission));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == permission.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(permission, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Permission"));

    private static Permission Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        Code = GetString(entity, "ECODE")!,
        DisplayName = GetString(entity, "ENAME")!,
        Description = GetString(entity, "Description"),
        ModuleId = GetString(entity, "ModuleId")!,
        Group = GetString(entity, "Group"),
        CreatedAt = GetDateTime(entity, "CreatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Permission permission)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = permission.Id
        };

        CopyToEntity(permission, entity);
        return entity;
    }

    private static void CopyToEntity(Permission permission, IDictionary<string, object?> entity)
    {
        // 领域对象仍暴露 Id；持久化真源切换后主键字段固定为 EID。
        entity["EID"] = permission.Id;
        entity["ECODE"] = permission.Code;
        entity["ENAME"] = permission.DisplayName;
        entity["Description"] = permission.Description;
        entity["ModuleId"] = permission.ModuleId;
        entity["Group"] = permission.Group;
        entity["CreatedAt"] = permission.CreatedAt;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;
}
