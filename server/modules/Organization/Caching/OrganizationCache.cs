using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Data;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>拥有四类组织档案及直接关系，实际保存结果经统一提交入口局部发布。</summary>
public class OrganizationCache(IServiceScopeFactory scopes) : NamedCache<OrganizationSnapshot>
{
    public override string Name => "Organization";

    /// <inheritdoc/>
    protected override long CountRecords(OrganizationSnapshot snapshot) =>
        (long)snapshot.Companies.Count + snapshot.Departments.Count + snapshot.Positions.Count + snapshot.Persons.Count;

    /// <inheritdoc/>
    protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        // 与写入共用基类协调权；Serializable 读事务使四表成为一个集合，避免分表查询的混合版本。
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var builder = new SnapshotBuilder(new OrganizationSnapshot());
        foreach (var model in new[] { "Company", "Department", "Position", "Person" })
        {
            await foreach (var row in db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model))
                .AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
                builder.Apply(new(model, (string)row["EID"]!, null, row));
        }
        var snapshot = builder.Build();
        OrganizationSnapshotValidation.ValidateAll(snapshot, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    /// <inheritdoc/>
    protected override ValueTask<OrganizationSnapshot> ApplyAsync(OrganizationSnapshot snapshot,
        IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        var builder = new SnapshotBuilder(snapshot);
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Apply(change);
        }
        var candidate = builder.Build();
        OrganizationSnapshotValidation.ValidateChanges(candidate, changes, cancellationToken);
        return ValueTask.FromResult(candidate);
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

    // 只复制不可变集合的受影响分支，自动跟踪和主动登记进入同一个模型分派与关系维护算法。
    private sealed class SnapshotBuilder(OrganizationSnapshot snapshot)
    {
        private readonly ImmutableDictionary<string, IReadOnlyDictionary<string, object?>>.Builder _companies = snapshot.Companies.ToBuilder();
        private readonly ImmutableDictionary<string, IReadOnlyDictionary<string, object?>>.Builder _departments = snapshot.Departments.ToBuilder();
        private readonly ImmutableDictionary<string, IReadOnlyDictionary<string, object?>>.Builder _positions = snapshot.Positions.ToBuilder();
        private readonly ImmutableDictionary<string, IReadOnlyDictionary<string, object?>>.Builder _persons = snapshot.Persons.ToBuilder();
        private readonly ImmutableDictionary<string, string>.Builder _companyCodes = snapshot.CompanyCodes.ToBuilder();
        private readonly ImmutableDictionary<(string CompanyId, string Code), string>.Builder _departmentCodes = snapshot.DepartmentCodes.ToBuilder();
        private readonly ImmutableDictionary<string, string>.Builder _personNumbers = snapshot.PersonNumbers.ToBuilder();
        private readonly ImmutableDictionary<string, string>.Builder _personByAccount = snapshot.PersonByAccount.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _departmentsByCompany = snapshot.DepartmentsByCompany.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _childDepartments = snapshot.ChildDepartments.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _personsByCompany = snapshot.PersonsByCompany.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _personsByDepartment = snapshot.PersonsByDepartment.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _personsByPosition = snapshot.PersonsByPosition.ToBuilder();

        // 从当前候选移除旧关系，再应用完整已保存记录；同事务多次保存按实际顺序折叠。
        public void Apply(PlatformRecordChange change)
        {
            var records = change.ModelKey switch
            {
                "Company" => _companies,
                "Department" => _departments,
                "Position" => _positions,
                "Person" => _persons,
                _ => throw new InvalidOperationException($"Unexpected organization model: {change.ModelKey}.")
            };
            if (records.TryGetValue(change.Id, out var before)) Index(change.ModelKey, change.Id, before, false);
            if (change.After is null) records.Remove(change.Id);
            else
            {
                var row = change.After.ToFrozenDictionary(StringComparer.Ordinal);
                if (!Equals(row["EID"], change.Id)) throw new InvalidOperationException("Organization record identity mismatch.");
                records[change.Id] = row;
                Index(change.ModelKey, change.Id, row, true);
            }
        }

        // 索引只保存标识及直接边；人员调动和部门换父不扫描或复制无关档案。
        private void Index(string model, string id, IReadOnlyDictionary<string, object?> row, bool add)
        {
            switch (model)
            {
                case "Company":
                    Unique(_companyCodes, (string)row["ECODE"]!, id, add);
                    break;
                case "Department":
                    var company = (string)row["CompanyId"]!;
                    Unique(_departmentCodes, (company, ((string)row["ECODE"]!).ToUpperInvariant()), id, add);
                    Relation(_departmentsByCompany, company, id, add);
                    Relation(_childDepartments, Text(row, "ParentDepartmentId"), id, add);
                    break;
                case "Person":
                    Unique(_personNumbers, (string)row["PersonNo"]!, id, add);
                    if (Text(row, "AccountEID") is { } account) Unique(_personByAccount, account, id, add);
                    Relation(_personsByCompany, Text(row, "CompanyId"), id, add);
                    Relation(_personsByDepartment, Text(row, "DepartmentId"), id, add);
                    Relation(_personsByPosition, Text(row, "PositionId"), id, add);
                    break;
            }
        }

        // 唯一键冲突使候选失败，不能用覆盖掩盖持久化关系损坏。
        private static void Unique<TKey>(ImmutableDictionary<TKey, string>.Builder index, TKey key, string id, bool add) where TKey : notnull
        {
            if (add) index.Add(key, id);
            else index.Remove(key);
        }

        // 空关系不建桶；移除最后一个成员时一并删除桶。
        private static void Relation(ImmutableDictionary<string, ImmutableHashSet<string>>.Builder index, string? key, string id, bool add)
        {
            if (key is null) return;
            var values = index.TryGetValue(key, out var current) ? current : ImmutableHashSet.Create<string>(StringComparer.Ordinal);
            values = add ? values.Add(id) : values.Remove(id);
            if (values.IsEmpty) index.Remove(key);
            else index[key] = values;
        }

        // 组装完整候选之后才交回基类原子发布。
        public OrganizationSnapshot Build() => new()
        {
            Companies = _companies.ToImmutable(), Departments = _departments.ToImmutable(),
            Positions = _positions.ToImmutable(), Persons = _persons.ToImmutable(),
            CompanyCodes = _companyCodes.ToImmutable(), DepartmentCodes = _departmentCodes.ToImmutable(),
            PersonNumbers = _personNumbers.ToImmutable(), PersonByAccount = _personByAccount.ToImmutable(),
            DepartmentsByCompany = _departmentsByCompany.ToImmutable(), ChildDepartments = _childDepartments.ToImmutable(),
            PersonsByCompany = _personsByCompany.ToImmutable(), PersonsByDepartment = _personsByDepartment.ToImmutable(),
            PersonsByPosition = _personsByPosition.ToImmutable()
        };
    }

    // 动态模型的可选关系使用 null，不将准备阶段转换为缺失记录。
    private static string? Text(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) as string;
}

/// <summary>模块启动尝试装载，缓存故障保留 Host；后续交给具名缓存的信号驱动恢复。</summary>
public sealed class OrganizationCacheInitializer(OrganizationCache cache, ILogger<OrganizationCacheInitializer> logger) : BackgroundService
{
    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await cache.InitializeAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportFailure(exception);
        }
        await base.StartAsync(cancellationToken);
    }
    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => cache.MaintainAsync(ReportFailure, stoppingToken);

    // 详细异常只进入宿主诊断日志，公开 readiness 不泄露数据库内容。
    private void ReportFailure(Exception exception) => logger.LogWarning(exception,
        "Cache {CacheName} is unavailable after {ElapsedMilliseconds} ms; recovery will retry.", cache.Name, cache.LastMaintenanceDuration?.TotalMilliseconds);
}
