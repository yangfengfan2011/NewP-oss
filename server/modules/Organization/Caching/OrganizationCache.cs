using System.Collections.Frozen;
using System.Collections.Immutable;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>一次完整只读组织结果；首片只启用已覆盖正式写入的公司资源。</summary>
public sealed record OrganizationSnapshot(ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Companies,
    ImmutableDictionary<string, string> CompanyCodes);

/// <summary>从模块服务作用域装载完整档案，并按实际变更局部维护公司及编码索引。</summary>
public class OrganizationCache(IServiceScopeFactory scopes) : NamedCache<OrganizationSnapshot>
{
    public override string Name => "Organization";

    /// <inheritdoc/>
    protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var companies = ImmutableDictionary.CreateBuilder<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        var codes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Company"))
            .AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var immutable = row.ToFrozenDictionary(StringComparer.Ordinal);
            var id = (string)immutable["EID"]!;
            companies.Add(id, immutable);
            codes.Add((string)immutable["ECODE"]!, id);
        }
        return new(companies.ToImmutable(), codes.ToImmutable());
    }

    /// <inheritdoc/>
    protected override ValueTask<OrganizationSnapshot> ApplyAsync(OrganizationSnapshot snapshot,
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        var companies = snapshot.Companies.ToBuilder();
        var codes = snapshot.CompanyCodes.ToBuilder();
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companies.TryGetValue(change.Id, out var before)) codes.Remove((string)before["ECODE"]!);
            if (change.After is null) companies.Remove(change.Id);
            else
            {
                var row = change.After.ToFrozenDictionary(StringComparer.Ordinal);
                companies[change.Id] = row;
                codes.Add((string)row["ECODE"]!, change.Id);
            }
        }
        return ValueTask.FromResult(new OrganizationSnapshot(companies.ToImmutable(), codes.ToImmutable()));
    }
}

/// <summary>模块启动时装载公司缓存；故障的自动恢复在后续恢复切片统一接入。</summary>
public sealed class OrganizationCacheInitializer(OrganizationCache cache) : IHostedService
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => cache.InitializeAsync(cancellationToken).AsTask();
    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
