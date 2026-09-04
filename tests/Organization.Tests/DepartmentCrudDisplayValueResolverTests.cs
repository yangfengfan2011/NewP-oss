using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentCrudDisplayValueResolverTests
{
    [Fact]
    public async Task ResolveListAsync_ResolvesCompanyAndParentDisplaysInOneBatchEach()
    {
        var directory = new RecordingCompanyDirectory(
        [
            new Company { Id = "Company_1", Name = "总部", IsEnabled = true },
            new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }
        ]);
        var departments = new RecordingDepartmentDirectory(
        [
            new Department { Id = "Department_Parent", Name = "研发中心", IsEnabled = true },
            new Department { Id = "Department_Disabled", Name = "旧中心", IsEnabled = false }
        ]);
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["ParentDepartmentId"] = "Department_Parent" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_2", ["ParentDepartmentId"] = "Department_Disabled" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["ParentDepartmentId"] = "Department_Parent" }
        };

        var descriptor = DepartmentCrudDescriptorFactory.Create();
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            rows,
            rows.Length,
            1,
            20,
            "SortOrder",
            "asc",
            ["CompanyId", "CompanyId_DisplayValue", "ParentDepartmentId", "ParentDepartmentId_DisplayValue"],
            descriptor.DisplayValueFields));
        var service = new StandardCrudService(
            repository: null!,
            executor,
            queryCustomizers: [],
            displayResolvers: [new DepartmentCrudDisplayValueResolver(directory, departments)],
            summaryProviders: [],
            validators: [],
            actionHandlers: [],
            deleteGuards: []);

        var result = await service.ListAsync(
            descriptor,
            new StandardCrudListQuery(Fields: ["CompanyId", "ParentDepartmentId"]));

        Assert.Equal(1, directory.BatchCallCount);
        Assert.Equal(1, departments.BatchCallCount);
        Assert.Equal(["Company_1", "Company_2"], directory.RequestedIds);
        Assert.Equal(["Department_Parent", "Department_Disabled"], departments.RequestedIds);
        Assert.Equal(
            ["CompanyId", "CompanyId_DisplayValue", "ParentDepartmentId", "ParentDepartmentId_DisplayValue"],
            executor.Request!.Fields);
        Assert.Equal("Company_1", result.Items[0]["CompanyId"]);
        Assert.Equal("Department_Parent", result.Items[0]["ParentDepartmentId"]);
        Assert.Equal("总部", result.Items[0]["CompanyId_DisplayValue"]);
        Assert.Equal("旧公司（已停用）", result.Items[1]["CompanyId_DisplayValue"]);
        Assert.Equal("研发中心", result.Items[0]["ParentDepartmentId_DisplayValue"]);
        Assert.Equal("旧中心（已停用）", result.Items[1]["ParentDepartmentId_DisplayValue"]);
    }

    private sealed class RecordingPlatformQueryExecutor(PlatformQueryResult result) : IPlatformQueryExecutor
    {
        public PlatformQueryRequest? Request { get; private set; }

        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingCompanyDirectory(IEnumerable<Company> companies) : ICompanyDirectory
    {
        private readonly IReadOnlyDictionary<string, Company> _companies =
            companies.ToDictionary(static company => company.Id, StringComparer.Ordinal);

        public int BatchCallCount { get; private set; }

        public IReadOnlyCollection<string> RequestedIds { get; private set; } = [];

        public Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
            IReadOnlyCollection<string> companyIds,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            RequestedIds = companyIds;
            return Task.FromResult<IReadOnlyDictionary<string, Company>>(
                companyIds.ToDictionary(id => id, id => _companies[id], StringComparer.Ordinal));
        }

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Company>> LookupAsync(
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingDepartmentDirectory(IEnumerable<Department> departments) : IDepartmentDirectory
    {
        private readonly IReadOnlyDictionary<string, Department> _departments =
            departments.ToDictionary(static department => department.Id, StringComparer.Ordinal);

        public int BatchCallCount { get; private set; }
        public IReadOnlyCollection<string> RequestedIds { get; private set; } = [];
        public Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
            IReadOnlyCollection<string> departmentIds,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            RequestedIds = departmentIds;
            return Task.FromResult<IReadOnlyDictionary<string, Department>>(
                departmentIds.ToDictionary(id => id, id => _departments[id], StringComparer.Ordinal));
        }
        public Task<Department?> GetByCompanyAndCodeAsync(string companyId, string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OrganizationDirectoryPage<Department>> LookupAsync(
            string companyId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
