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
        var query = new RecordingOrganizationQuery(
            companies: [new Company { Id = "Company_1", Name = "总部" }, new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }],
            departments: [new Department { Id = "Department_Parent", Name = "研发中心" }, new Department { Id = "Department_Disabled", Name = "旧中心", IsEnabled = false }]);
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["ParentDepartmentId"] = "Department_Parent" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_2", ["ParentDepartmentId"] = "Department_Disabled" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["ParentDepartmentId"] = "Department_Parent" }
        };

        var descriptor = OrganizationCrudTestResources.Resolve(DepartmentCrudDescriptorFactory.Create());
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
            new ICP.Platform.Testing.StandardCrudTestAccess(), null, null!,
            executor,
            new StandardCrudDefaultWriter(new ICP.Platform.Testing.StandardCrudTestAccess(), null!, [], [], objectIds: new ICP.Platform.Crud.ObjectIds.PlatformObjectIdGenerator()),
            [],
            new DisplayValueProjection([new DepartmentCrudDisplayValueResolver(query, new AllowAllReadableIds())]),
            [], objectScope: new ICP.Platform.Testing.StandardCrudTestAccess());

        var result = await service.ListAsync(
            descriptor,
            new StandardCrudListQuery(Fields: ["CompanyId", "ParentDepartmentId"]));

        Assert.Equal(1, query.Reads);
        Assert.Equal(2, query.Batches.Count);
        Assert.Equal(["Company_1", "Company_2"], query.Batches[ICP.Platform.Organization.Contracts.OrganizationRecordKind.Company]);
        Assert.Equal(["Department_Parent", "Department_Disabled"], query.Batches[ICP.Platform.Organization.Contracts.OrganizationRecordKind.Department]);
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

}
