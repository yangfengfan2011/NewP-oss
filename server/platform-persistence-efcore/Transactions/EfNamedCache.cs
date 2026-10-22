using System.Collections.Immutable;
using System.Data;
using System.Runtime.CompilerServices;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Persistence.EfCore.Transactions;

/// <summary>按固定模型清单从共享 DbContext 一致装载与按 EID 重读；具体缓存只拥有快照构建、校验和计数。</summary>
public abstract class EfNamedCache<TSnapshot> : NamedCache<TSnapshot> where TSnapshot : class
{
    private readonly ImmutableArray<string> _models;

    /// <summary>模型清单同时决定装载顺序，应与提交登记使用同一份声明。</summary>
    protected EfNamedCache(IServiceScopeFactory scopes, ImmutableArray<string> models)
    {
        Scopes = scopes;
        _models = models;
    }

    /// <summary>供局部构造等需要作用域服务的覆写创建自己的作用域。</summary>
    protected IServiceScopeFactory Scopes { get; }

    /// <summary>
    /// 从完整记录流构造并校验快照。记录流与 <paramref name="services"/> 只在本次调用内有效，
    /// 不能保存或带出；返回后基类才提交读事务并释放作用域。
    /// </summary>
    protected abstract ValueTask<TSnapshot> BuildAsync(IAsyncEnumerable<PlatformRecordChange> records,
        IServiceProvider services, CancellationToken cancellationToken);

    /// <inheritdoc/>
    protected override async ValueTask<TSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        // 与写入共用基类协调权；Serializable 读事务使多表成为一个集合，避免分表查询的混合版本。
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var snapshot = await BuildAsync(ReadAll(db, cancellationToken), scope.ServiceProvider, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    /// <inheritdoc/>
    protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        await using var scope = Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var result = new List<PlatformRecordChange>();
        foreach (var group in changes.GroupBy(change => change.ModelKey))
        {
            var ids = group.Select(change => change.Id).Distinct(StringComparer.Ordinal).ToArray();
            var rows = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(group.Key)).AsNoTracking()
                .Where(row => ids.Contains(EF.Property<string>(row, "EID"))).ToListAsync(cancellationToken);
            var byId = rows.ToDictionary(row => (string)row["EID"]!, StringComparer.Ordinal);
            // 已删除记录以空 After 表达，交给 ApplyAsync 移除。
            foreach (var id in ids) result.Add(new(group.Key, id, null, byId.GetValueOrDefault(id)));
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    // 按清单顺序逐行流式交出完整记录，不先物化整表。
    private async IAsyncEnumerable<PlatformRecordChange> ReadAll(EfPlatformDbContext db,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var model in _models)
        {
            await foreach (var row in db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model))
                .AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
                yield return new(model, (string)row["EID"]!, null, row);
        }
    }
}
