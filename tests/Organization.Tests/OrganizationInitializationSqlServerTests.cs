using System.Diagnostics;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Audit.Persistence.Descriptors;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationInitializationSqlServerTests(ITestOutputHelper output)
{
    private const string Run = "NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION";

    [EnvironmentGatedFact(Run, "受控补齐账号候选沿真实 SQL 限定读取片段")]
    public async Task OwnershipCandidates_SearchesOneBoundedSqlFragmentAndAttachesCurrentDepartmentInOneBatch()
    {
        await using var lease = await ReusableSqlServerDatabase.AcquireAsync();
        var sql = new List<string>();
        await using var provider = Provider(lease.ConnectionString, sql);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await db.Database.EnsureCreatedAsync();
        for (var index = 0; index < 301; index++)
        {
            var id = $"candidate{index:D4}";
            db.Set<Dictionary<string, object?>>("IdentityAccount").Add(new()
            {
                ["EID"] = id, ["ECODE"] = id, ["NormalizedECODE"] = id.ToUpperInvariant(), ["ENAME"] = id,
                ["Status"] = "Enabled", ["IsLocalAdministrator"] = false, ["IsPersonnelBound"] = false,
                ["PersonnelDisableHold"] = false, ["Revision"] = 1L
            });
        }
        foreach (var id in new[] { "cändidate-only", "ordered-a", "ordered_a" })
            db.Set<Dictionary<string, object?>>("IdentityAccount").Add(new()
            {
                ["EID"] = id, ["ECODE"] = id, ["NormalizedECODE"] = id.ToUpperInvariant(), ["ENAME"] = id,
                ["Status"] = "Enabled", ["IsLocalAdministrator"] = false, ["IsPersonnelBound"] = false,
                ["PersonnelDisableHold"] = false, ["Revision"] = 1L
            });
        await db.SaveChangesAsync();
        var cache = provider.GetRequiredService<OrganizationCache>();
        await cache.InitializeAsync();
        var directory = new OrganizationAccountOwnershipDirectory(new IdentityAccountDirectory(new EfIdentityRepository(db)), new OrganizationQuery(cache), new Readable());
        sql.Clear();
        var allocated = GC.GetTotalAllocatedBytes();
        var timer = Stopwatch.StartNew();
        var candidates = await directory.SearchAsync("  CANDIDATE  ");
        timer.Stop();
        var commands = sql.Where(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal)).ToArray();
        Assert.Equal(100, candidates.Count);
        Assert.Equal("candidate0000", candidates[0].AccountId);
        Assert.All(candidates, candidate => Assert.Null(candidate.DepartmentId));
        Assert.Equal(2, commands.Length);
        Assert.Contains("TOP", commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", commands[0], StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"candidate-search source=304 matched=301 returned={candidates.Count} sql={commands.Length} elapsedMs={timer.Elapsed.TotalMilliseconds:F2} allocatedBytes={GC.GetTotalAllocatedBytes() - allocated}");
        Assert.Equal("cändidate-only", Assert.Single(await directory.SearchAsync("CÄNDIDATE")).AccountId);
        Assert.Equal(new[] { "ordered-a", "ordered_a" }, (await directory.SearchAsync("ordered")).Select(candidate => candidate.Code));
    }

    [EnvironmentGatedFact(Run, "默认组织的真实 SQL 初始化及可信归属")]
    public async Task Initialization_IsRetryablePreservesBusinessOrganizationAndAssignsAdministratorWithoutPerson()
    {
        await using var lease = await ReusableSqlServerDatabase.AcquireAsync();
        var sql = new List<string>();
        await using var provider = Provider(lease.ConnectionString, sql);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Set<Dictionary<string, object?>>("Company").Add(Row("business-company", "BUSINESS", "已有公司"));
        var department = Row("business-department", "DEPT", "已有部门");
        department["CompanyId"] = "business-company";
        department["ParentDepartmentId"] = null;
        db.Set<Dictionary<string, object?>>("Department").Add(department);
        db.Set<Dictionary<string, object?>>("Person").Add(new()
        {
            ["EID"] = "business-person", ["PersonNo"] = "P1", ["ENAME"] = "既有人员", ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true, ["CompanyId"] = "business-company", ["DepartmentId"] = "business-department", ["AccountEID"] = "ordinary"
        });
        foreach (var (id, admin, bound, status) in new[]
        {
            ("administrator", true, false, "Enabled"), ("ordinary", false, true, "Enabled"),
            ("unbound", false, false, "Enabled"), ("deleted", false, false, "Deleted")
        })
            db.Set<Dictionary<string, object?>>("IdentityAccount").Add(new()
            {
                ["EID"] = id, ["ECODE"] = id, ["NormalizedECODE"] = id.ToUpperInvariant(), ["ENAME"] = id,
                ["Status"] = status, ["IsLocalAdministrator"] = admin, ["IsPersonnelBound"] = bound,
                ["PersonnelDisableHold"] = false, ["Revision"] = 1L
            });
        await db.SaveChangesAsync();
        var cache = provider.GetRequiredService<OrganizationCache>();
        await cache.InitializeAsync();
        using var activity = new Activity("organization-initialization-sql").Start();
        var recorder = new BusinessAuditRecorder(db, scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>(), new PlatformObjectIdGenerator());
        var service = Service(db, cache, recorder, scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>());
        sql.Clear();
        var timer = Stopwatch.StartNew();
        var first = await service.InitializeAsync();
        timer.Stop();
        output.WriteLine($"organization-initialization existingCompanies=1 existingDepartments=1 newCompanies=1 newDepartments=1 sql={sql.Count(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal))} elapsedMs={timer.Elapsed.TotalMilliseconds:F2}");
        Assert.True(first.CompanyCreated);
        Assert.True(first.DepartmentCreated);
        sql.Clear();
        timer.Restart();
        var retry = await service.InitializeAsync();
        timer.Stop();
        output.WriteLine($"organization-initialization-retry newCompanies=0 newDepartments=0 sql={sql.Count(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal))} elapsedMs={timer.Elapsed.TotalMilliseconds:F2}");
        Assert.Equal(first.CompanyId, retry.CompanyId);
        Assert.Equal(first.DepartmentId, retry.DepartmentId);
        Assert.False(retry.CompanyCreated);
        Assert.False(retry.DepartmentCreated);
        Assert.Equal(2, await db.Set<Dictionary<string, object?>>("Company").CountAsync());
        Assert.Equal(2, await db.Set<Dictionary<string, object?>>("Department").CountAsync());
        Assert.Equal(1, await db.Set<Dictionary<string, object?>>("Person").CountAsync());
        var changes = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.Equal("administrator", change["ActorId"]));
        var companyCreatedAt = (await db.Set<Dictionary<string, object?>>("Company").SingleAsync(row => EF.Property<string>(row, "EID") == first.CompanyId))["CreatedAt"];
        Assert.All(changes, change => Assert.Equal(companyCreatedAt, change["OccurredAt"]));
        Assert.Equal(companyCreatedAt, (await db.Set<Dictionary<string, object?>>("Department").SingleAsync(row => EF.Property<string>(row, "EID") == first.DepartmentId))["CreatedAt"]);
        Assert.Contains(changes, change => Equals(change["TargetId"], first.CompanyId));
        Assert.Contains(changes, change => Equals(change["TargetId"], first.DepartmentId));

        var query = new OrganizationQuery(cache);
        var directory = new OrganizationAccountOwnershipDirectory(new IdentityAccountDirectory(new EfIdentityRepository(db)), query, new Readable());
        var ownership = await directory.FindManyAsync(["administrator", "ordinary", "unbound", "unknown", "deleted"]);
        Assert.Equal(3, ownership.Count);
        Assert.Equal(first.DepartmentId, ownership["administrator"].DepartmentId);
        Assert.Equal("business-department", ownership["ordinary"].DepartmentId);
        Assert.Null(ownership["unbound"].DepartmentId);
        var display = new OrganizationAccountOwnershipDirectory(new IdentityAccountDirectory(new EfIdentityRepository(db)), query, new DenyBusinessDepartment());
        var names = await display.FindDepartmentNamesAsync([first.DepartmentId, "business-department", "missing"]);
        Assert.Equal("系统管理组", names[first.DepartmentId]);
        Assert.Equal("***", names["business-department"]);
        Assert.Equal(2, names.Count);
        var admission = new OrganizationAdmissionPolicy(query);
        Assert.True(await admission.IsAllowedAsync(new() { AccountEid = "administrator", ECODE = "admin", ENAME = "admin", IsLocalAdministrator = true, Status = IdentityAccountStatus.Enabled }));
        Assert.True(await admission.IsAllowedAsync(new() { AccountEid = "ordinary", ECODE = "ordinary", ENAME = "ordinary", IsPersonnelBound = true, Status = IdentityAccountStatus.Enabled }));
        Assert.False(await admission.IsAllowedAsync(new() { AccountEid = "unbound", ECODE = "unbound", ENAME = "unbound", Status = IdentityAccountStatus.Enabled }));
        Assert.Null((await query.ReadAsync()).FindByAccount("administrator"));
    }

    [EnvironmentGatedFact(Run, "初始化失败和取消不得留下半组织或成功记录")]
    public async Task Initialization_RollsBackBothOrganizationAndBusinessRecordsOnFailureOrCancellation()
    {
        foreach (var cancel in new[] { false, true })
        {
            await using var lease = await ReusableSqlServerDatabase.AcquireAsync();
            await using var provider = Provider(lease.ConnectionString);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await db.Database.EnsureCreatedAsync();
            var cache = provider.GetRequiredService<OrganizationCache>();
            await cache.InitializeAsync();
            using var activity = new Activity("organization-initialization-rollback").Start();
            var service = Service(db, cache, new FailingRecorder(db, new BusinessAuditRecorder(db, scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>(), new PlatformObjectIdGenerator()), cancel), scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>());
            var timer = Stopwatch.StartNew();
            if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(async () => await service.InitializeAsync());
            else await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.InitializeAsync());
            timer.Stop();
            output.WriteLine($"organization-initialization-rollback cancel={cancel} flushedRows=3 retainedRows=0 elapsedMs={timer.Elapsed.TotalMilliseconds:F2}");
            Assert.Empty(await db.Set<Dictionary<string, object?>>("Company").AsNoTracking().ToListAsync());
            Assert.Empty(await db.Set<Dictionary<string, object?>>("Department").AsNoTracking().ToListAsync());
            Assert.Empty(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().ToListAsync());
            Assert.Null((await new OrganizationQuery(cache).ReadAsync()).FindCompanyByCode("NO_INFIGHTING"));
        }
    }

    [EnvironmentGatedFact(Run, "初始化冲突保留已有业务组织")]
    public async Task Initialization_RejectsSameNameUnderAnotherCodeWithoutOverwritingOrDuplicating()
    {
        await using var lease = await ReusableSqlServerDatabase.AcquireAsync();
        await using var provider = Provider(lease.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Set<Dictionary<string, object?>>("Company").Add(Row("existing", "OTHER", "No Infighting Company"));
        await db.SaveChangesAsync();
        var cache = provider.GetRequiredService<OrganizationCache>();
        await cache.InitializeAsync();
        var service = Service(db, cache, new BusinessAuditRecorder(db, scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>(), new PlatformObjectIdGenerator()), scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>());
        await Assert.ThrowsAsync<OrganizationInitializationConflictException>(async () => await service.InitializeAsync());
        Assert.Equal("OTHER", (await db.Set<Dictionary<string, object?>>("Company").SingleAsync())["ECODE"]);
        Assert.Empty(await db.Set<Dictionary<string, object?>>("Department").ToListAsync());
    }

    private static ServiceProvider Provider(string connectionString, List<string>? sql = null)
    {
        var builder = new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
        if (sql is not null) builder.LogTo(sql.Add, LogLevel.Information);
        var options = builder.Options;
        var definitions = OrganizationModelDefinitions.All.Concat(IdentityModelDefinitions.All)
            .Concat(new AuditModelMetadataProvider().GetDefinitions()).ToArray();
        var services = new ServiceCollection();
        services.AddScoped<IEntityAuditValueProvider>(_ => new EntityAuditValueProvider(new Stamp()));
        services.AddScoped(sp => new EfPlatformDbContext(options, definitions, sp.GetRequiredService<IEntityAuditValueProvider>()));
        services.AddSingleton<OrganizationCache>();
        return services.BuildServiceProvider();
    }

    private static OrganizationInitializationService Service(EfPlatformDbContext db, OrganizationCache cache, IBusinessAuditRecorder recorder, IEntityAuditValueProvider audit) =>
        new(db, new EfPlatformTransactionExecutor(db, [new PlatformCommitRegistration("Company", cache), new PlatformCommitRegistration("Department", cache)]),
            new PlatformObjectIdGenerator(), recorder, audit);

    private static Dictionary<string, object?> Row(string id, string code, string name) => new()
    { ["EID"] = id, ["ECODE"] = code, ["ENAME"] = name, ["SortOrder"] = "0", ["IsEnabled"] = true };

    private sealed class Stamp : IEntityAuditStampProvider
    {
        public static DateTime Time { get; } = new(2026, 10, 17, 1, 2, 3, DateTimeKind.Utc);
        private long _ticks;
        public EntityAuditStamp GetCurrent() => new("administrator", Time.AddTicks(Interlocked.Increment(ref _ticks)));
    }

    private sealed class Readable : IDataScopeReadableIds
    {
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(objectIds.ToHashSet());
    }

    private sealed class DenyBusinessDepartment : IDataScopeReadableIds
    {
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(objectIds.Where(id => id != "business-department").ToHashSet());
    }

    private sealed class FailingRecorder(EfPlatformDbContext db, IBusinessAuditRecorder inner, bool cancel) : IBusinessAuditRecorder
    {
        public async ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default)
        {
            await inner.RegisterAsync(change, cancellationToken);
            // 故障发生在真实 SQL 写入后、平台统一提交前，证明已落库变化与业务记录一起回滚。
            await db.SaveChangesAsync(cancellationToken);
            if (cancel) throw new OperationCanceledException();
            throw new InvalidOperationException("injected-initialization-failure");
        }
    }
}
