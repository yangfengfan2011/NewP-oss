using System.Data;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Authorization.Caching;

/// <summary>规则两表共用具名缓存、写协调及提交发布屏障；不缓存身份或最终权限。</summary>
public class DataScopeRuleCache(IServiceScopeFactory scopes) : NamedCache<DataScopeRuleSnapshot>
{
    internal const string NodeModel = "DataScopeRuleNode";
    internal const string SubjectModel = "DataScopeRuleSubject";
    public override string Name => "Authorization.DataScopeRules";

    /// <inheritdoc/>
    protected override long CountRecords(DataScopeRuleSnapshot snapshot) => snapshot.RecordCount;

    /// <inheritdoc/>
    protected override async ValueTask<DataScopeRuleSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        // Serializable 两表读事务与缓存写协调共同保证完整集合，包含停用节点及全部主体。
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var nodes = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(NodeModel)).AsNoTracking().ToListAsync(cancellationToken);
        var subjects = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(SubjectModel)).AsNoTracking().ToListAsync(cancellationToken);
        var changes = nodes.Select(row => new PlatformRecordChange(NodeModel, (string)row["EID"]!, null, row))
            .Concat(subjects.Select(row => new PlatformRecordChange(SubjectModel, (string)row["EID"]!, null, row)));
        var snapshot = DataScopeRuleSnapshot.Empty.Apply(changes, scope.ServiceProvider.GetRequiredService<DataScopeCatalog>(), cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    /// <inheritdoc/>
    protected override async ValueTask<DataScopeRuleSnapshot> ApplyAsync(DataScopeRuleSnapshot snapshot,
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return snapshot.Apply(changes, scope.ServiceProvider.GetRequiredService<DataScopeCatalog>(), cancellationToken);
    }

    /// <inheritdoc/>
    protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
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
            foreach (var id in ids) result.Add(new(group.Key, id, null, byId.GetValueOrDefault(id)));
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

/// <summary>启动尝试完整装载，初载故障保留 Host 并交给信号驱动的自动恢复。</summary>
public sealed class DataScopeRuleCacheInitializer(DataScopeRuleCache cache, ILogger<DataScopeRuleCacheInitializer> logger) : BackgroundService
{
    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await cache.InitializeAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { ReportFailure(exception); }
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => cache.MaintainAsync(ReportFailure, stoppingToken);

    // 完整异常只进入宿主日志；公开状态沿已有具名缓存就绪边界。
    private void ReportFailure(Exception exception) => logger.LogWarning(exception,
        "Cache {CacheName} is unavailable after {ElapsedMilliseconds} ms; recovery will retry.", cache.Name, cache.LastMaintenanceDuration?.TotalMilliseconds);
}
