using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Query.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentCrudDisplayValueResolverTests
{
    [Fact]
    public async Task ResolveListAsync_ResolvesCompanyDisplayInOneBatchAndMarksDisabledCompany()
    {
        var directory = new RecordingCompanyDirectory(
        [
            new Company { Id = "Company_1", Name = "总部", IsEnabled = true },
            new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }
        ]);
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_2" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1" }
        };

        var result = await new DepartmentCrudDisplayValueResolver(directory).ResolveListAsync(
            DepartmentCrudDescriptorFactory.Create(),
            rows,
            [new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")]);

        Assert.Equal(1, directory.BatchCallCount);
        Assert.Equal(["Company_1", "Company_2"], directory.RequestedIds);
        Assert.Equal("总部", result[0]["CompanyId_DisplayValue"]);
        Assert.Equal("旧公司（已停用）", result[1]["CompanyId_DisplayValue"]);
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
}
