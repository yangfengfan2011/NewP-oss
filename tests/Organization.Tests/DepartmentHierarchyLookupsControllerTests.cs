using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentHierarchyLookupsControllerTests
{
    [Fact]
    public async Task Parents_WhenHierarchyExceedsSupportedBound_ReturnsStableConflictProblem()
    {
        var directory = new LimitExceededDirectory();
        var controller = new DepartmentHierarchyLookupsController(directory, directory);

        var action = await controller.Parents("Company_1");

        var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal(DepartmentHierarchyLimits.ErrorCode, problem.Extensions["code"]);
        Assert.Equal(
            DepartmentHierarchyLimits.MaximumDepartmentsPerCompany,
            problem.Extensions["maximumDepartments"]);
    }

    private sealed class LimitExceededDirectory : IDepartmentDirectory, IDepartmentHierarchyDirectory
    {
        public Task<Department?> GetByIdAsync(
            string departmentId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
            IReadOnlyCollection<string> departmentIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Department?> GetByCompanyAndCodeAsync(
            string companyId,
            string code,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Department>> LookupAsync(
            string companyId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Department>> LookupParentCandidatesAsync(
            string companyId,
            string? currentDepartmentId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<OrganizationDirectoryPage<Department>>(
                new DepartmentHierarchyLimitExceededException());
    }
}
