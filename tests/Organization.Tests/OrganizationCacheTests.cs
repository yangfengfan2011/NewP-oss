using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationCacheTests
{
    [Theory]
    [InlineData("missing-company")]
    [InlineData("cycle")]
    [InlineData("half-affiliation")]
    [InlineData("missing-position")]
    public async Task InitialLoad_RejectsBrokenRelationshipsWithoutPublishingPartialSnapshot(string corruption)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        await using var provider = services.BuildServiceProvider();
        await using var db = new EfPlatformDbContext(options, definitions);
        db.Set<Dictionary<string, object?>>("Organization/Company").Add(Row("company"));
        var department = Row("department");
        department["CompanyId"] = corruption == "missing-company" ? "missing" : "company";
        department["ParentDepartmentId"] = corruption == "cycle" ? "department" : null;
        db.Set<Dictionary<string, object?>>("Organization/Department").Add(department);
        db.Set<Dictionary<string, object?>>("Organization/Person").Add(new()
        {
            ["EID"] = "person", ["PersonNo"] = "P", ["ENAME"] = "人员", ["EmploymentStatus"] = "Active", ["IsEnabled"] = false,
            ["CompanyId"] = corruption == "half-affiliation" ? "company" : null,
            ["PositionId"] = corruption == "missing-position" ? "missing" : null
        });
        await db.SaveChangesAsync();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.InitializeAsync().AsTask());
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => cache.ReadAsync().AsTask());
    }

    [Fact]
    public async Task DepartmentCommit_DoesNotReplaceCompanyWithSameId()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        await using var provider = services.BuildServiceProvider();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await using var db = new EfPlatformDbContext(options, definitions);
        var executor = new EfPlatformTransactionExecutor(db, new[] { "Company", "Department", "Position", "Person" }
            .Select(model => new PlatformCommitRegistration($"Organization/{model}", cache)));
        await executor.ExecuteAsync(["Company", "Department"], token =>
        {
            db.Set<Dictionary<string, object?>>("Organization/Company").Add(Row("shared"));
            var department = Row("shared");
            department["CompanyId"] = "shared";
            department["ENAME"] = "部门";
            db.Set<Dictionary<string, object?>>("Organization/Department").Add(department);
            return ValueTask.FromResult(true);
        });
        Assert.Equal("shared", (await cache.ReadAsync()).Companies["shared"]["ENAME"]);
    }

    [Fact]
    public async Task CompanyCommit_PublishesCompleteImmutableRecordsAndRetainsOldResult()
    {
        var services = new ServiceCollection();
        // 本测试使用真实 Company authoring 与完整投影，数据库原子性另由 SQL Server 测试证明。
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        services.AddScoped(_ => new EfPlatformDbContext(options, OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray()));
        await using var provider = services.BuildServiceProvider();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        var old = await cache.ReadAsync();
        await using var db = new EfPlatformDbContext(options, OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray());
        var executor = new EfPlatformTransactionExecutor(db, [new PlatformCommitRegistration("Organization/Company", cache)]);
        await executor.ExecuteAsync(["Company"], async token =>
        {
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Company")).Add(new()
            { ["EID"] = "company", ["ECODE"] = "C", ["ENAME"] = "完整公司", ["SortOrder"] = "01", ["IsEnabled"] = false });
            await db.SaveChangesAsync(token);
            Assert.Empty((await cache.ReadAsync(token)).Companies);
            return true;
        });
        var current = await cache.ReadAsync();
        var company = Assert.Single(current.Companies).Value;
        Assert.Equal("完整公司", company["ENAME"]);
        Assert.Equal(false, company["IsEnabled"]);
        Assert.Equal("01", company["SortOrder"]);
        Assert.NotNull(company["CreatedAt"]);
        Assert.NotNull(company["CreatedBy"]);
        Assert.Empty(old.Companies);
        var restarted = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await restarted.InitializeAsync();
        Assert.Equal(false, (await restarted.ReadAsync()).Companies["company"]["IsEnabled"]);
        Assert.Equal(company["CreatedAt"], (await restarted.ReadAsync()).Companies["company"]["CreatedAt"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)company).Add("intrusion", true));
    }
    [Fact]
    public async Task ConcurrentCompanyCommits_PreserveBothChangesAndRejectUndeclaredModels()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        await using var provider = services.BuildServiceProvider();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await using var firstDb = new EfPlatformDbContext(options, definitions);
        await using var secondDb = new EfPlatformDbContext(options, definitions);
        var registration = new PlatformCommitRegistration("Organization/Company", cache);
        var first = new EfPlatformTransactionExecutor(firstDb, [registration]);
        var second = new EfPlatformTransactionExecutor(secondDb, [registration]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWrite = first.ExecuteAsync(["Company"], async token =>
        {
            firstDb.Set<Dictionary<string, object?>>("Organization/Company").Add(Row("one"));
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return true;
        }).AsTask();
        await entered.Task;
        var secondEntered = false;
        var secondWrite = second.ExecuteAsync(["Company"], token =>
        {
            secondEntered = true;
            secondDb.Set<Dictionary<string, object?>>("Organization/Company").Add(Row("two"));
            return ValueTask.FromResult(true);
        }).AsTask();
        Assert.False(secondEntered);
        release.SetResult();
        await Task.WhenAll(firstWrite, secondWrite);
        Assert.Equal(2, (await cache.ReadAsync()).Companies.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ExecuteAsync(token =>
        {
            firstDb.Set<Dictionary<string, object?>>("Organization/Company").Add(Row("undeclared"));
            return ValueTask.FromResult(true);
        }).AsTask());
        Assert.Equal(2, (await cache.ReadAsync()).Companies.Count);
    }

    private static Dictionary<string, object?> Row(string id) => new()
    {
        ["EID"] = id, ["ECODE"] = id, ["ENAME"] = id, ["SortOrder"] = "01", ["IsEnabled"] = true
    };

}
