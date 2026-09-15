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

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("RolePermission"));

}
