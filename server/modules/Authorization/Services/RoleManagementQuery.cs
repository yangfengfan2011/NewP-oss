using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>标准角色列表只物化当前请求页，不在应用层裁剪全部角色。</summary>
public sealed class RoleManagementQuery(EfPlatformDbContext db)
{
    /// <summary>按名称或代码筛选，固定稳定排序并在数据库完成计数及分页。</summary>
    public async Task<object> ListAsync(string? keyword, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize <= 0 ? 20 : pageSize;
        var page = Math.Clamp(pageIndex, 1, int.MaxValue / size);
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(r => EF.Property<string>(r, "ECODE").Contains(keyword) || EF.Property<string>(r, "ENAME").Contains(keyword));
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(r => EF.Property<string>(r, "ECODE")).ThenBy(r => EF.Property<string>(r, "EID"))
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new { items, total, pageIndex = page, pageSize = size, sortBy = "ECODE", sortDirection = "asc" };
    }
}
