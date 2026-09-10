using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Organization.Directories;
/// <summary>组织模块拥有其名称目录，Audit 不读取组织私有 EF 仓储。</summary>
public sealed class BusinessAuditOrganizationNames(EfPlatformDbContext db, string kind, string model) : IBusinessAuditNameDirectory
{
    public string Kind => kind;
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking()
            .Where(r => ids.Contains(EF.Property<string>(r, "EID")))
            .Select(r => new { Id = EF.Property<string>(r, "EID"), Name = EF.Property<string>(r, "ENAME") }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Name, StringComparer.Ordinal);
    }
}
