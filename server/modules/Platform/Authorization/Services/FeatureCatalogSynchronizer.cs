using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整候选准备成功后，在单一事务同步目录差异及管理员分配。</summary>
public sealed class FeatureCatalogSynchronizer(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IPlatformObjectIdGenerator ids, ActiveFeatureCatalog catalog) : IFeatureCatalogSynchronizer
{
    /// <summary>先批量清理失效位置及分配，再新增索引并收敛管理员全集；提交成功后才发布。</summary>
    public async Task SynchronizeAsync(IReadOnlyList<FeaturePoint> points, CancellationToken cancellationToken = default)
    {
        var pending = new List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>();
        Dictionary<string, string> indexed;
        try
        {
            indexed = await transactions.ExecuteAsync(async token =>
            {
                var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"));
                var existing = await rows.AsNoTracking().Select(row => new { Id = EF.Property<string>(row, "EID"), Key = EF.Property<string>(row, "PositionKey") }).ToListAsync(token);
                var current = existing.ToDictionary(row => row.Key, row => row.Id, StringComparer.Ordinal);
                var candidateKeys = points.Select(point => point.Position).ToHashSet(StringComparer.Ordinal);
                var deletedIds = existing.Where(row => !candidateKeys.Contains(row.Key)).Select(row => row.Id).ToArray();
                var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
                if (deletedIds.Length > 0)
                {
                    // 反向索引定位受影响分配，在数据库批量删除，不物化其他角色的关系。
                    await grants.Where(row => deletedIds.Contains(EF.Property<string>(row, "FunctionPointId"))).ExecuteDeleteAsync(token);
                    await rows.Where(row => deletedIds.Contains(EF.Property<string>(row, "EID"))).ExecuteDeleteAsync(token);
                    foreach (var key in current.Keys.Where(key => !candidateKeys.Contains(key)).ToArray()) current.Remove(key);
                }
                foreach (var point in points)
                {
                    if (current.ContainsKey(point.Position)) continue;
                    var id = ids.NewId("FunctionPoint");
                    pending.Add(rows.Add(new() { ["EID"] = id, ["PositionKey"] = point.Position }));
                    current.Add(point.Position, id);
                }
                var role = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking()
                    .SingleAsync(row => EF.Property<string>(row, "ECODE") == LocalAdministratorRole.Code, token);
                var roleId = (string)role["EID"]!;
                var oldIds = (await grants.AsNoTracking().Where(row => EF.Property<string>(row, "RoleId") == roleId)
                    .Select(row => EF.Property<string>(row, "FunctionPointId")).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
                var targetIds = points.Select(point => current[point.Position]).ToHashSet(StringComparer.Ordinal);

                foreach (var id in targetIds.Except(oldIds)) pending.Add(grants.Add(new()
                {
                    ["EID"] = ids.NewId("RoleFunctionGrant"), ["RoleId"] = roleId, ["FunctionPointId"] = id,
                    ["GrantedAt"] = DateTime.UtcNow, ["GrantedBy"] = "system"
                }));
                // 完整候选已准备成功；旧目录与分配在同一同步事务清空，不触及新功能授权。
                // 旧表仅保留历史迁移兼容，退出旧写入口后空表自身就是已完成切换的事实。
                foreach (var legacyModel in new[] { "RolePermission", "Permission" })
                {
                    var legacy = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(legacyModel));
                    if (await legacy.AnyAsync(token)) await legacy.ExecuteDeleteAsync(token);
                }
                return current;
            }, cancellationToken);
        }
        catch
        {
            // 事务回滚不会撤销EF跟踪状态；只释放本次新增项，保留调用方其他实体。
            foreach (var entry in pending) entry.State = EntityState.Detached;
            throw;
        }
        catalog.Publish(points, indexed);
    }
}
