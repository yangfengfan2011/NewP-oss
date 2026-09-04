using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationDirectoryTests
{
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
        var directory = new EfCompanyDirectory(dbContext);

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

        var companies = await new EfCompanyDirectory(dbContext)
            .GetByIdsAsync(["Company_1", "Company_2", "Company_1"]);
        var departments = await new EfDepartmentDirectory(dbContext)
            .GetByIdsAsync(["Department_1", "Department_2", "Department_1"]);

        Assert.Equal(2, companies.Count);
        Assert.Equal(2, departments.Count);
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
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
