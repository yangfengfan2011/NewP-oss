using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class DepartmentServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("1A2F373C-3D57-43D1-87C7-82B24DE4C2E0");

    [Fact]
    public async Task CreateDepartment_WithUniqueSiblingCode_CreatesDepartment()
    {
        var service = BuildService();

        var result = await service.CreateAsync(new DepartmentDto
        {
            CompanyId = CompanyId,
            Code = "ROOT",
            Name = "根部门",
            SortOrder = 1
        });

        Assert.Equal(CompanyId, result.CompanyId);
        Assert.Equal("ROOT", result.Code);
        Assert.Equal("根部门", result.Name);
        Assert.Null(result.ParentDepartmentId);
        Assert.True(result.IsEnabled);
        Assert.Equal(1, result.SortOrder);
    }

    [Fact]
    public async Task CreateDepartment_WithDuplicateSiblingCode_ThrowsInvalidOperationException()
    {
        var service = BuildService();

        await service.CreateAsync(new DepartmentDto
        {
            CompanyId = CompanyId,
            Code = "OPS",
            Name = "运营部"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new DepartmentDto
            {
                CompanyId = CompanyId,
                Code = "OPS",
                Name = "重复运营部"
            }));
    }

    [Fact]
    public async Task UpdateDepartment_WithCircularParent_ThrowsInvalidOperationException()
    {
        var service = BuildService();
        var root = await service.CreateAsync(new DepartmentDto
        {
            CompanyId = CompanyId,
            Code = "ROOT",
            Name = "根部门"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(root.Id, new DepartmentDto
            {
                CompanyId = CompanyId,
                ParentDepartmentId = root.Id,
                Name = "根部门"
            }));
    }

    [Fact]
    public async Task UpdateDepartment_ValidRequest_UpdatesDepartment()
    {
        var service = BuildService();
        var root = await service.CreateAsync(new DepartmentDto
        {
            CompanyId = CompanyId,
            Code = "ROOT",
            Name = "根部门"
        });

        var child = await service.CreateAsync(new DepartmentDto
        {
            CompanyId = CompanyId,
            ParentDepartmentId = root.Id,
            Code = "ENG",
            Name = "工程部"
        });

        var updated = await service.UpdateAsync(child.Id, new DepartmentDto
        {
            CompanyId = CompanyId,
            ParentDepartmentId = root.Id,
            Name = "平台工程部",
            IsEnabled = false,
            SortOrder = 3
        });

        Assert.Equal(child.Id, updated.Id);
        Assert.Equal("ENG", updated.Code);
        Assert.Equal("平台工程部", updated.Name);
        Assert.Equal(root.Id, updated.ParentDepartmentId);
        Assert.False(updated.IsEnabled);
        Assert.Equal(3, updated.SortOrder);
    }

    private static IDepartmentService BuildService() => new DepartmentService(new InMemoryDepartmentRepository());

    private sealed class InMemoryDepartmentRepository : IDepartmentRepository
    {
        private readonly List<Department> _departments = [];

        public Task<List<Department>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.Where(department => department.CompanyId == companyId).Select(Clone).ToList());

        public Task<Department?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.FirstOrDefault(department => department.Id == departmentId) is { } department ? Clone(department) : null);

        public Task<bool> ExistsSiblingCodeAsync(
            Guid companyId,
            Guid? parentDepartmentId,
            string code,
            Guid? excludedDepartmentId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = _departments.Any(department =>
                department.CompanyId == companyId &&
                department.ParentDepartmentId == parentDepartmentId &&
                department.Id != excludedDepartmentId &&
                string.Equals(department.Code, code, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(exists);
        }

        public Task CreateAsync(Department department, CancellationToken cancellationToken = default)
        {
            _departments.Add(Clone(department));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Department department, CancellationToken cancellationToken = default)
        {
            var index = _departments.FindIndex(existing => existing.Id == department.Id);
            if (index >= 0)
            {
                _departments[index] = Clone(department);
            }

            return Task.CompletedTask;
        }

        private static Department Clone(Department department) => new()
        {
            Id = department.Id,
            CompanyId = department.CompanyId,
            ParentDepartmentId = department.ParentDepartmentId,
            Code = department.Code,
            Name = department.Name,
            IsEnabled = department.IsEnabled,
            SortOrder = department.SortOrder,
            CreatedAt = department.CreatedAt,
            UpdatedAt = department.UpdatedAt
        };
    }
}
