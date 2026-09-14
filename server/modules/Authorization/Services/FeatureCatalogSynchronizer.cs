using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整候选准备成功后的非破坏同步；删除收口由目录生命周期步骤扩展。</summary>
public sealed class FeatureCatalogSynchronizer(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IPlatformObjectIdGenerator ids, ActiveFeatureCatalog catalog) : IFeatureCatalogSynchronizer
{
    /// <summary>事务内新增索引并收敛管理员全集，提交成功后才发布；不回收候选未包含的旧位置。</summary>
    public async Task SynchronizeAsync(IReadOnlyList<FeaturePoint> points, CancellationToken cancellationToken = default)
    {
        var indexed = await transactions.ExecuteAsync(async token =>
        {
            var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"));
            var existing = await rows.AsNoTracking().Select(row => new { Id = EF.Property<string>(row, "EID"), Key = EF.Property<string>(row, "PositionKey") }).ToListAsync(token);
            var current = existing.ToDictionary(row => row.Key, row => row.Id, StringComparer.Ordinal);
            var candidateKeys = points.Select(point => point.Position).ToHashSet(StringComparer.Ordinal);
            if (current.Keys.Any(key => !candidateKeys.Contains(key)))
                throw new InvalidOperationException("当前目录含待删除位置，必须先完成目录删除生命周期验证。");
            foreach (var point in points)
            {
                if (current.ContainsKey(point.Position)) continue;
                var id = ids.NewId("FunctionPoint");
                rows.Add(new() { ["EID"] = id, ["PositionKey"] = point.Position });
                current.Add(point.Position, id);
            }
            var role = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "ECODE") == "LocalAdmin", token);
            var roleId = (string)role["EID"]!;
            var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
            var old = await grants.Where(row => EF.Property<string>(row, "RoleId") == roleId).ToListAsync(token);
            var targetIds = points.Select(point => current[point.Position]).ToHashSet(StringComparer.Ordinal);
            var oldIds = old.Select(row => (string)row["FunctionPointId"]!).ToHashSet(StringComparer.Ordinal);
            grants.RemoveRange(old.Where(row => !targetIds.Contains((string)row["FunctionPointId"]!)));
            foreach (var id in targetIds.Except(oldIds)) grants.Add(new()
            {
                ["EID"] = ids.NewId("RoleFunctionGrant"), ["RoleId"] = roleId, ["FunctionPointId"] = id,
                ["GrantedAt"] = DateTime.UtcNow, ["GrantedBy"] = "system"
            });
            return current;
        }, cancellationToken);
        catalog.Publish(points, indexed);
    }
}
