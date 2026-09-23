using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Authorization.Contracts;
using System.Linq.Expressions;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationDirectoryTests
{
    [Fact]
    public async Task CompanyDirectory_SelectsCurrentSqlForEveryBusinessReadAndReturnsToPublishedFacts()
    {
        await using var dbContext = CreateDbContext();
        await SeedCompaniesAsync(dbContext, [Company("company", "MiXeD", "已发布名称", enabled: true)]);
        var published = await CreateQueryAsync(dbContext);
        var transactions = new OrganizationFactsTestServices.TransactionState();
        var facts = new OrganizationEffectiveFacts(transactions, new PublishedOrganizationFacts(published),
            new SqlOrganizationFacts(dbContext, new SqlServerDepartmentHierarchyQuery(dbContext)));
        var directory = new EfCompanyDirectory(dbContext, facts);

        var stored = await dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company"))
            .SingleAsync();
        stored["ENAME"] = "当前 SQL 名称";
        stored["IsEnabled"] = false;
        await dbContext.SaveChangesAsync();
        await SeedCompaniesAsync(dbContext, [Company("new-company", "NEW", "新公司", enabled: true)]);

        // 同一 owner 每次读取时选择来源；此低层测试只证明选择，实际事务隔离与回滚由既有 SQL nested 测试证明。
        Assert.Equal("已发布名称", (await directory.GetByIdAsync("company"))?.Name);
        Assert.Null(await directory.GetByIdAsync("new-company"));
        Assert.True(await facts.IsCompanyEnabledAsync("company", default));
        transactions.IsActive = true;
        Assert.Equal("当前 SQL 名称", (await directory.GetByIdAsync("company"))?.Name);
        Assert.Equal("当前 SQL 名称", (await directory.GetByCodeAsync("  MIXED  "))?.Name);
        Assert.NotNull(await directory.GetByIdAsync("new-company"));
        Assert.False(await facts.IsCompanyEnabledAsync("company", default));
        var current = await directory.GetByIdsAsync(["company", "new-company", "company", " ", "missing"]);
        Assert.Equal(2, current.Count);
        Assert.Equal("当前 SQL 名称", current["company"].Name);
        Assert.Empty(await directory.GetByIdsAsync([]));

        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company")).Remove(stored);
        await dbContext.SaveChangesAsync();
        Assert.Null(await directory.GetByIdAsync("company"));
        Assert.Null(await directory.GetByCodeAsync("MiXeD"));
        Assert.Empty(await directory.GetByIdsAsync(["company"]));

        transactions.IsActive = false;
        Assert.Equal("已发布名称", (await directory.GetByCodeAsync("  MIXED  "))?.Name);
        var original = await directory.GetByIdsAsync(["company", "new-company", "company", " ", "missing"]);
        Assert.Equal("company", Assert.Single(original).Key);
        Assert.True(original["company"].IsEnabled);
    }

    [Fact]
    public async Task CompanyDirectory_LookupPagesEnabledCandidatesAndResolvesDisabledCurrentValue()
    {
        await using var dbContext = CreateDbContext();
        await SeedCompaniesAsync(dbContext,
        [
            Company("Company_3", "C003", "已停用公司", enabled: false),
            Company("Company_2", "C002", "B研发", enabled: true),
            Company("Company_1", "C001", "A研发", enabled: true)
        ]);
        var directory = new EfCompanyDirectory(dbContext, OrganizationFactsTestServices.Published(new RecordingOrganizationQuery()));

        var page = await directory.LookupAsync(new OrganizationDirectoryLookupRequest(
            Keyword: "研发",
            Page: 1,
            PageSize: 1,
            CurrentValueIds: ["Company_3"]));

        Assert.Equal("Company_1", Assert.Single(page.Items).Id);
        Assert.Equal("Company_3", Assert.Single(page.CurrentItems).Id);
        Assert.True(page.HasMore);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
    }

    [Fact]
    public async Task DepartmentDirectory_LookupScopesCandidatesAndCurrentValuesToCompany()
    {
        await using var dbContext = CreateDbContext();
        await SeedDepartmentsAsync(dbContext,
        [
            Department("Department_1", "Company_1", "D001", "研发一部", enabled: true),
            Department("Department_2", "Company_1", "D002", "研发二部", enabled: false),
            Department("Department_3", "Company_2", "D003", "研发三部", enabled: true)
        ]);
        var directory = new EfDepartmentDirectory(dbContext);

        var page = await directory.LookupAsync(
            "Company_1",
            new OrganizationDirectoryLookupRequest(
                Keyword: "研发",
                Page: 1,
                PageSize: 20,
                CurrentValueIds: ["Department_2", "Department_3"]));

        Assert.Equal("Department_1", Assert.Single(page.Items).Id);
        Assert.Equal("Department_2", Assert.Single(page.CurrentItems).Id);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task LookupDirectories_FilterCandidatesAndMaskOnlyUnreadableCurrentNames()
    {
        await using var dbContext = CreateDbContext();
        await SeedCompaniesAsync(dbContext, [
            Company("Company_1", "C001", "Secret company", true),
            Company("Company_2", "C002", "***", true)]);
        await SeedDepartmentsAsync(dbContext, [
            Department("Department_1", "Company_2", "D001", "Secret department", true),
            Department("Department_2", "Company_2", "D002", "***", true)]);
        var plans = new TestReadPlans();
        var companies = new EfCompanyDirectory(dbContext,
            OrganizationFactsTestServices.Published(new RecordingOrganizationQuery()), plans);
        var departments = new EfDepartmentDirectory(dbContext, new StubHierarchyQuery(
            new DepartmentHierarchyCandidatePage([], false)), plans);

        var companyPage = await companies.LookupAsync(new OrganizationDirectoryLookupRequest(null, 1, 1,
            ["Company_1", "Company_2"]));
        var departmentPage = await departments.LookupAsync("Company_2",
            new OrganizationDirectoryLookupRequest(null, 1, 1, ["Department_1", "Department_2"]));

        Assert.Equal("Company_2", Assert.Single(companyPage.Items).Id);
        Assert.Equal("Department_2", Assert.Single(departmentPage.Items).Id);
        Assert.Contains("Company_1", companyPage.MaskedCurrentIds!);
        Assert.Contains("Department_1", departmentPage.MaskedCurrentIds!);
        Assert.Equal("***", OrganizationLookupHttpProjection.Map(companyPage, false)
            .CurrentItems.Single(item => item.Value == "Company_1").Label);
        Assert.Equal("***", OrganizationLookupHttpProjection.Map(departmentPage, false)
            .CurrentItems.Single(item => item.Value == "Department_1").Label);
        Assert.Equal("***", companyPage.CurrentItems.Single(item => item.Id == "Company_2").Name);
        Assert.Equal("***", departmentPage.CurrentItems.Single(item => item.Id == "Department_2").Name);
        Assert.False(Assert.Single(OrganizationLookupHttpProjection.Map(companyPage, false).Items).Disabled);
        Assert.False(Assert.Single(OrganizationLookupHttpProjection.Map(departmentPage, false).Items).Disabled);
        plans.ReadDisabled = true;
        Assert.Equal("Secret company", (await companies.LookupAsync(new OrganizationDirectoryLookupRequest(null, 1, 2,
            ["Company_1"]))).CurrentItems.Single().Name);
        Assert.Equal("Secret department", (await departments.LookupAsync("Company_2",
            new OrganizationDirectoryLookupRequest(null, 1, 2, ["Department_1"]))).CurrentItems.Single().Name);
        plans.Failure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => companies.LookupAsync(
            new OrganizationDirectoryLookupRequest(null, 1, 2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => departments.LookupAsync("Company_2",
            new OrganizationDirectoryLookupRequest(null, 1, 2)));
    }

    private sealed class TestReadPlans : IDataScopeReadPlanProvider
    {
        public bool ReadDisabled { get; set; }
        public bool Failure { get; set; }
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            if (Failure) throw new InvalidOperationException("read plan unavailable");
            if (ReadDisabled) return Task.FromResult(new DataScopeReadPlan(null));
            Expression<Func<Dictionary<string, object?>, bool>> predicate = resourceKey switch
            {
                "organization.company" => row => EF.Property<string>(row, "EID") != "Company_1",
                "organization.department" => row => EF.Property<string>(row, "EID") != "Department_1",
                _ => throw new InvalidOperationException("Unexpected resource")
            };
            return Task.FromResult(new DataScopeReadPlan(predicate));
        }
    }

    [Fact]
    public async Task CompanyLookup_BatchesMoreThanOneHundredCurrentIds()
    {
        await using var dbContext = CreateDbContext();
        var ids = Enumerable.Range(0, 105).Select(index => $"Company_{index:D3}").ToArray();
        await SeedCompaniesAsync(dbContext, ids.Select(id => Company(id, id, id, true)).ToArray());
        var directory = new EfCompanyDirectory(dbContext,
            OrganizationFactsTestServices.Published(new RecordingOrganizationQuery()));

        var page = await directory.LookupAsync(new OrganizationDirectoryLookupRequest(null, 1, 1, ids));

        Assert.Equal(ids.Length, page.CurrentItems.Count);
        Assert.Equal(ids, page.CurrentItems.Select(item => item.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DepartmentDirectory_ParentLookupCombinesBoundedHierarchyPageWithCurrentValue()
    {
        await using var dbContext = CreateDbContext();
        await SeedDepartmentsAsync(dbContext,
        [
            Department("Department_Root", "Company_1", "D001", "根", enabled: true),
            Department("Department_Child", "Company_1", "D002", "子", enabled: true, "Department_Root"),
            Department("Department_Grandchild", "Company_1", "D003", "孙", enabled: true, "Department_Child"),
            Department("Department_Peer", "Company_1", "D004", "同级", enabled: true),
            Department("Department_Disabled", "Company_1", "D005", "停用", enabled: false),
            Department("Department_OtherCompany", "Company_2", "D006", "跨公司", enabled: true)
        ]);
        var directory = new EfDepartmentDirectory(
            dbContext,
            new StubHierarchyQuery(
                new DepartmentHierarchyCandidatePage(
                    [Department("Department_Peer", "Company_1", "D004", "同级", enabled: true)],
                    HasMore: false)));

        var page = await directory.LookupParentCandidatesAsync(
            "Company_1",
            "Department_Root",
            new OrganizationDirectoryLookupRequest(null, 1, 1, ["Department_Disabled"]));

        Assert.Equal("Department_Peer", Assert.Single(page.Items).Id);
        Assert.Equal("Department_Disabled", Assert.Single(page.CurrentItems).Id);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task DepartmentDirectory_ParentLookupWithoutCurrentDepartmentUsesOrdinaryLookupRules()
    {
        await using var dbContext = CreateDbContext();
        await SeedDepartmentsAsync(dbContext,
        [
            Department("Department_1", "Company_1", "D001", "A研发", enabled: true),
            Department("Department_2", "Company_1", "D002", "B研发", enabled: true),
            Department("Department_Disabled", "Company_1", "D003", "旧研发", enabled: false),
            Department("Department_OtherCompany", "Company_2", "D004", "跨公司研发", enabled: true)
        ]);
        var hierarchyQuery = new StubHierarchyQuery(new DepartmentHierarchyCandidatePage([], HasMore: false));
        var directory = new EfDepartmentDirectory(dbContext, hierarchyQuery);
        var request = new OrganizationDirectoryLookupRequest(
            Keyword: "研发",
            Page: 1,
            PageSize: 1,
            CurrentValueIds: ["Department_Disabled", "Department_OtherCompany"]);

        var ordinary = await directory.LookupAsync("Company_1", request);
        var parent = await directory.LookupParentCandidatesAsync("Company_1", null, request);

        Assert.Equal(ordinary.Items.Select(static item => item.Id), parent.Items.Select(static item => item.Id));
        Assert.Equal(
            ordinary.CurrentItems.Select(static item => item.Id),
            parent.CurrentItems.Select(static item => item.Id));
        Assert.Equal(ordinary.Page, parent.Page);
        Assert.Equal(ordinary.PageSize, parent.PageSize);
        Assert.Equal(ordinary.HasMore, parent.HasMore);
        Assert.Equal(0, hierarchyQuery.LookupCallCount);
    }

    [Fact]
    public async Task DirectoriesResolveDistinctBatchesWithoutPerIdReads()
    {
        await using var dbContext = CreateDbContext();
        await SeedCompaniesAsync(dbContext,
        [
            Company("Company_1", "C001", "总部", enabled: true),
            Company("Company_2", "C002", "分部", enabled: false)
        ]);
        await SeedDepartmentsAsync(dbContext,
        [
            Department("Department_1", "Company_1", "D001", "研发部", enabled: true),
            Department("Department_2", "Company_2", "D002", "旧部门", enabled: false)
        ]);

        var companies = await new EfCompanyDirectory(dbContext, OrganizationFactsTestServices.Published(await CreateQueryAsync(dbContext)))
            .GetByIdsAsync(["Company_1", "Company_2", "Company_1"]);
        var departments = await new EfDepartmentDirectory(dbContext)
            .GetByIdsAsync(["Department_1", "Department_2", "Department_1"]);

        Assert.Equal(2, companies.Count);
        Assert.Equal(2, departments.Count);
    }

    private static async Task<ICP.Platform.Organization.Contracts.IOrganizationQuery> CreateQueryAsync(EfPlatformDbContext source)
    {
        var options = (DbContextOptions<EfPlatformDbContext>)source.GetService<IDbContextOptions>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization", SchemaName = "dbo" }).ToArray()));
        await using var provider = services.BuildServiceProvider();
        var cache = new ICP.Platform.Modules.Organization.Caching.OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        return new ICP.Platform.Modules.Organization.Caching.OrganizationQuery(cache);
    }

    private static EfPlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var definitions = OrganizationModelDefinitions.All
            .Select(static definition => definition with
            {
                OwnerModuleId = "Organization",
                SchemaName = "dbo"
            })
            .ToArray();
        return new EfPlatformDbContext(options, definitions);
    }

    private static Company Company(string id, string code, string name, bool enabled) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        IsEnabled = enabled
    };

    private static Department Department(
        string id,
        string companyId,
        string code,
        string name,
        bool enabled,
        string? parentDepartmentId = null) => new()
    {
        Id = id,
        CompanyId = companyId,
        Code = code,
        Name = name,
        IsEnabled = enabled,
        ParentDepartmentId = parentDepartmentId
    };

    private static async Task SeedCompaniesAsync(EfPlatformDbContext dbContext, IEnumerable<Company> companies)
    {
        foreach (var company in companies)
        {
            dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company")).Add(
                new Dictionary<string, object?>
                {
                    ["EID"] = company.Id,
                    ["ECODE"] = company.Code,
                    ["ENAME"] = company.Name,
                    ["IsEnabled"] = company.IsEnabled,
                    ["SortOrder"] = company.SortOrder,
                    ["CreatedBy"] = "User_Admin",
                    ["CreatedAt"] = DateTime.UtcNow,
                    ["UpdatedBy"] = "User_Admin",
                    ["UpdatedAt"] = DateTime.UtcNow
                });
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedDepartmentsAsync(EfPlatformDbContext dbContext, IEnumerable<Department> departments)
    {
        foreach (var department in departments)
        {
            dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Department")).Add(
                new Dictionary<string, object?>
                {
                    ["EID"] = department.Id,
                    ["CompanyId"] = department.CompanyId,
                    ["ParentDepartmentId"] = department.ParentDepartmentId,
                    ["ECODE"] = department.Code,
                    ["ENAME"] = department.Name,
                    ["IsEnabled"] = department.IsEnabled,
                    ["SortOrder"] = department.SortOrder,
                    ["CreatedBy"] = "User_Admin",
                    ["CreatedAt"] = DateTime.UtcNow,
                    ["UpdatedBy"] = "User_Admin",
                    ["UpdatedAt"] = DateTime.UtcNow
                });
        }

        await dbContext.SaveChangesAsync();
    }

    private sealed class StubHierarchyQuery(DepartmentHierarchyCandidatePage result) : IDepartmentHierarchyQuery
    {
        public int LookupCallCount { get; private set; }

        public Task<DepartmentParentRelationship> ClassifyParentAsync(
            string companyId,
            string currentDepartmentId,
            string candidateParentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DepartmentHierarchyCandidatePage> LookupParentCandidatesAsync(
            string companyId,
            string currentDepartmentId,
            string? keyword,
            int page,
            int pageSize,
            bool includeDisabled,
            System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>>? readPredicate,
            CancellationToken cancellationToken)
        {
            LookupCallCount++;
            return Task.FromResult(result);
        }
    }
}
