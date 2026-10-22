using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Ownership;

/// <summary>通知只请求已注册规范对象的安全当前名称；底层私表访问留在共享描述符消费者内。</summary>
public sealed class EntityOwnershipTransferDisplay(EfPlatformDbContext db, IDataScopeResourceCatalog resources,
    IStandardCrudResourceRegistry registry, IDataScopeReadableIds readableIds, IDataScopeObjectAccess access)
{
    /// <summary>批量交集正常 read 及 owner 约束后读取名称；隐藏、已删除或无显示字段的目标省略，不用原 ID 兜底。</summary>
    public async Task<IReadOnlyDictionary<EntityOwnershipTransferTarget, string>> FindNamesAsync(
        IReadOnlyCollection<EntityOwnershipTransferTarget> targets, CancellationToken token)
    {
        var result = new Dictionary<EntityOwnershipTransferTarget, string>();
        foreach (var group in targets.Distinct().GroupBy(target => target.ResourceKey))
        {
            var resource = resources.Resources.SingleOrDefault(resource => resource.ResourceKey == group.Key);
            if (resource is null) continue;
            foreach (var batch in group.Chunk(100))
            {
                var ids = batch.Select(target => target.ObjectId).ToArray();
                var readable = await readableIds.GetReadableIdsAsync(resource.ResourceKey, ids, token);
                var allowed = new List<string>();
                foreach (var id in readable)
                    if (resource.ReadConstraintKey is null || await access.IsReadableAsync(resource.ResourceKey, id, token)) allowed.Add(id);
                if (allowed.Count == 0) continue;
                var descriptor = registry.FindByResourceKey(resource.ResourceKey);
                var displayField = descriptor?.DisplayField ?? "ENAME";
                if (db.Model.FindEntityType(resource.ModelName)?.FindProperty(displayField) is null) continue;
                var rows = await db.Set<Dictionary<string, object?>>(resource.ModelName).AsNoTracking()
                    .Where(row => EF.Parameter(allowed).Contains(EF.Property<string>(row, resource.IdField)))
                    .Select(row => new { Id = EF.Property<string>(row, resource.IdField), Name = EF.Property<string?>(row, displayField) }).ToArrayAsync(token);
                foreach (var row in rows)
                    if (row.Name is not null) result.Add(new(resource.ResourceKey, row.Id), row.Name);
            }
        }
        return result;
    }
}
