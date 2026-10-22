using System.Collections.Immutable;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authorization.Caching;

/// <summary>规则两表共用具名缓存、写协调及提交发布屏障；不缓存身份或最终权限。</summary>
public class DataScopeRuleCache(IServiceScopeFactory scopes) : EfNamedCache<DataScopeRuleSnapshot>(scopes, Models)
{
    internal const string NodeModel = "DataScopeRuleNode";
    internal const string SubjectModel = "DataScopeRuleSubject";
    internal const string CacheName = "Authorization.DataScopeRules";
    /// <summary>装载与提交登记共用的模型清单；包含停用节点及全部主体。</summary>
    internal static readonly ImmutableArray<string> Models = [NodeModel, SubjectModel];
    public override string Name => CacheName;

    /// <inheritdoc/>
    protected override long CountRecords(DataScopeRuleSnapshot snapshot) => snapshot.RecordCount;

    /// <inheritdoc/>
    protected override async ValueTask<DataScopeRuleSnapshot> BuildAsync(IAsyncEnumerable<PlatformRecordChange> records,
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var builder = new DataScopeRuleSnapshot.Builder(DataScopeRuleSnapshot.Empty);
        await foreach (var record in records.WithCancellation(cancellationToken)) builder.Add(record, cancellationToken);
        return builder.Build(services.GetRequiredService<DataScopeCatalog>(), cancellationToken);
    }

    /// <inheritdoc/>
    protected override async ValueTask<DataScopeRuleSnapshot> ApplyAsync(DataScopeRuleSnapshot snapshot,
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        await using var scope = Scopes.CreateAsyncScope();
        return snapshot.Apply(changes, scope.ServiceProvider.GetRequiredService<DataScopeCatalog>(), cancellationToken);
    }
}
