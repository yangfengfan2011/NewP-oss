using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence.Ef;

public sealed class EfUserRoleRepository : IUserRoleRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfUserRoleRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<UserRole>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .Where(item => EF.Property<string>(item, "UserId") == userId)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task CreateBatchAsync(IEnumerable<UserRole> userRoles, CancellationToken cancellationToken = default)
    {
        var entities = userRoles.Select(ToEntity).ToList();
        Entities.AddRange(entities);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .Where(item => EF.Property<string>(item, "UserId") == userId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0)
        {
            return;
        }

        Entities.RemoveRange(entities);
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

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("UserRole");

    private static UserRole Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID")!,
        UserId = GetString(entity, "UserId")!,
        RoleId = GetString(entity, "RoleId")!,
        AssignedAt = GetDateTime(entity, "AssignedAt"),
        AssignedBy = GetString(entity, "AssignedBy")
    };

    private static Dictionary<string, object?> ToEntity(UserRole item) => new(StringComparer.Ordinal)
    {
        // UserRole 已按 metadata 投影为实体，关系对象自身主键固定为 EID。
        ["EID"] = item.Id,
        ["UserId"] = item.UserId,
        ["RoleId"] = item.RoleId,
        ["AssignedAt"] = item.AssignedAt,
        ["AssignedBy"] = item.AssignedBy
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime
            ? dateTime
            : default;
}
