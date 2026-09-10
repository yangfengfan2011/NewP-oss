using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>角色名称由拥有模块批量解析，角色删除后 Audit 保留稳定 ID。</summary>
public sealed class BusinessAuditRoleNames(EfPlatformDbContext db) : IBusinessAuditNameDirectory
{
    public string Kind => "authorization.role";
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking()
            .Where(r => ids.Contains(EF.Property<string>(r, "EID")))
            .Select(r => new { Id = EF.Property<string>(r, "EID"), Name = EF.Property<string>(r, "ENAME") }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Name, StringComparer.Ordinal);
    }
}
