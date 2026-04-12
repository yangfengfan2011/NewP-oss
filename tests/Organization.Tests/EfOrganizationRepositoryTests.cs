using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class EfOrganizationRepositoryTests
{
    [Fact]
    public async Task EfCompanyRepository_CanCreateAndUpdateCompany()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfCompanyRepository(dbContext);

        var company = new Company
        {
            Code = "ICP",
            Name = "研发中心",
            SortOrder = 1
        };

        await repository.CreateAsync(company);
        company.Name = "研发中心（更新）";
        company.SortOrder = 2;
        company.IsEnabled = false;
        company.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateAsync(company);

        var saved = await repository.GetByIdAsync(company.Id);

        Assert.NotNull(saved);
        Assert.Equal("ICP", saved!.Code);
        Assert.Equal("研发中心（更新）", saved.Name);
        Assert.False(saved.IsEnabled);
        Assert.Equal(2, saved.SortOrder);
    }

    [Fact]
    public async Task EfDepartmentRepository_ExistsSiblingCodeAsync_RespectsParentScope()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfDepartmentRepository(dbContext);
        var companyId = Guid.NewGuid();
        var rootId = Guid.NewGuid();

        await repository.CreateAsync(new Department
        {
            Id = rootId,
            CompanyId = companyId,
            Code = "ROOT",
            Name = "根部门"
        });

        await repository.CreateAsync(new Department
        {
            CompanyId = companyId,
            ParentDepartmentId = rootId,
            Code = "ENG",
            Name = "工程部"
        });

        var duplicateUnderSameParent = await repository.ExistsSiblingCodeAsync(companyId, rootId, "ENG");
        var sameCodeAtRoot = await repository.ExistsSiblingCodeAsync(companyId, null, "ENG");

        Assert.True(duplicateUnderSameParent);
        Assert.False(sameCodeAtRoot);
    }

    [Fact]
    public async Task EfPositionRepository_CanCreateAndLoadPosition()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfPositionRepository(dbContext);
        var departmentId = Guid.NewGuid();

        var position = new Position
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = departmentId,
            Code = "LEAD",
            Name = "组长",
            EmploymentStatus = EmploymentStatus.FullTime,
            SortOrder = 5
        };

        await repository.CreateAsync(position);

        var saved = await repository.GetByIdAsync(position.Id);
        var positions = await repository.GetByDepartmentIdAsync(departmentId);

        Assert.NotNull(saved);
        Assert.Equal("LEAD", saved!.Code);
        Assert.Single(positions);
        Assert.Equal(position.Id, positions[0].Id);
    }

    [Fact]
    public void OrganizationModule_ConfigureServices_RegistersRepositoriesAndServices()
    {
        var services = new ServiceCollection();
        var module = new OrganizationModule();

        module.ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICompanyRepository) &&
            descriptor.ImplementationType == typeof(EfCompanyRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDepartmentRepository) &&
            descriptor.ImplementationType == typeof(EfDepartmentRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPositionRepository) &&
            descriptor.ImplementationType == typeof(EfPositionRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICompanyService) &&
            descriptor.ImplementationType == typeof(CompanyService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDepartmentService) &&
            descriptor.ImplementationType == typeof(DepartmentService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPositionService) &&
            descriptor.ImplementationType == typeof(PositionService));
    }

    private static EfPlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new EfPlatformDbContext(options, OrganizationModelDefinitions.All);
    }
}
