using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence.Ef;

public sealed class EfRolePermissionRepository : IRolePermissionRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfRolePermissionRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<RolePermission>> GetByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .Where(item => EF.Property<string>(item, "RoleId") == roleId)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<List<RolePermission>> GetByRoleIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await Entities
            .AsNoTracking()
            .Where(item => ids.Contains(EF.Property<string>(item, "RoleId")))
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task CreateBatchAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken cancellationToken = default)
    {
        var entities = rolePermissions.Select(ToEntity).ToList();
        Entities.AddRange(entities);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .Where(item => EF.Property<string>(item, "RoleId") == roleId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0)
        {
            return;
        }

        Entities.RemoveRange(entities);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("RolePermission");

    private static RolePermission Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        RoleId = GetString(entity, "RoleId")!,
        PermissionId = GetString(entity, "PermissionId")!,
        GrantedAt = GetDateTime(entity, "GrantedAt"),
        GrantedBy = GetString(entity, "GrantedBy")
    };

    private static Dictionary<string, object?> ToEntity(RolePermission item) => new(StringComparer.Ordinal)
    {
        // RolePermission 已按 metadata 投影为实体，关系对象自身主键固定为 EID。
        ["EID"] = item.Id,
        ["RoleId"] = item.RoleId,
        ["PermissionId"] = item.PermissionId,
        ["GrantedAt"] = item.GrantedAt,
        ["GrantedBy"] = item.GrantedBy
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;
}
