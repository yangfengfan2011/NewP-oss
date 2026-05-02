using System.Collections;
using System.Reflection;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class PersonListContractTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DepartmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void PersonsController_DeclaresListAndDisableEndpoints()
    {
        var controllerType = typeof(PersonsController);

        var listAction = controllerType.GetMethod("GetList");
        var disableAction = controllerType.GetMethod("Disable");

        Assert.NotNull(listAction);
        Assert.NotNull(disableAction);
        Assert.Contains(listAction!.GetCustomAttributes<HttpGetAttribute>(), attribute => attribute.Template is null);
        Assert.Contains(disableAction!.GetCustomAttributes<HttpPostAttribute>(), attribute => attribute.Template == "{id:guid}/disable");
    }

    [Fact]
    public void IPersonService_DeclaresListAndDisableContracts()
    {
        var serviceType = typeof(IPersonService);

        Assert.NotNull(serviceType.GetMethod("GetListAsync"));
        Assert.NotNull(serviceType.GetMethod("DisableAsync"));
    }

    [Fact]
    public void IPersonRepository_DeclaresListContract()
    {
        var repositoryType = typeof(IPersonRepository);

        Assert.NotNull(repositoryType.GetMethod("GetListAsync"));
    }

    [Fact]
    public void PersonListItemDto_ExposesRequiredListColumns()
    {
        var dtoType = GetOrganizationAssembly().GetType("ICP.Platform.Modules.Organization.DTOs.PersonListItemDto");

        Assert.NotNull(dtoType);
        Assert.NotNull(dtoType!.GetProperty("Id"));
        Assert.NotNull(dtoType.GetProperty("PersonNo"));
        Assert.NotNull(dtoType.GetProperty("Name"));
        Assert.NotNull(dtoType.GetProperty("CompanyName"));
        Assert.NotNull(dtoType.GetProperty("DepartmentName"));
        Assert.NotNull(dtoType.GetProperty("EmploymentStatus"));
        Assert.NotNull(dtoType.GetProperty("IsEnabled"));
    }

    [Fact]
    public void PersonDetailDto_ExposesStatusizedSummaryBlocks()
    {
        var assembly = GetOrganizationAssembly();
        var detailType = assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonDetailDto");
        var summaryType = assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonSummaryBlockDto");

        Assert.NotNull(detailType);
        Assert.NotNull(summaryType);
        Assert.NotNull(detailType!.GetProperty("UserLinkSummary"));
        Assert.NotNull(detailType.GetProperty("AuthorizationSummary"));
        Assert.Equal(summaryType, detailType.GetProperty("UserLinkSummary")!.PropertyType);
        Assert.Equal(summaryType, detailType.GetProperty("AuthorizationSummary")!.PropertyType);
        Assert.NotNull(summaryType!.GetProperty("Status"));
        Assert.NotNull(summaryType.GetProperty("Data"));
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresQueryDisplaySummaryAndConditionalDelete()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Equal("organization.person", descriptor.ResourceKey);
        Assert.Equal("Person", descriptor.ModelName);
        Assert.Equal("api/organization/persons", descriptor.Route);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
        Assert.Contains("CompanyName", descriptor.DisplayValueFields);
        Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
        Assert.Contains("userLink", descriptor.SummaryBlockKeys);
        Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
    }

    [Fact]
    public void PersonCrudQueryCustomizer_MapsKeywordToPersonNoAndName()
    {
        var customizer = new PersonCrudQueryCustomizer();
        var conditions = customizer.BuildConditions(
            PersonCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "张" }));

        Assert.Contains(conditions, condition => condition.Field == "PersonNo" && condition.Operator == "contains");
        Assert.Contains(conditions, condition => condition.Field == "ENAME" && condition.Operator == "contains");
    }

    [Fact]
    public async Task PersonCrudDeleteGuard_WhenPersonHasUserLink_ReturnsConflict()
    {
        var personId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var guard = new PersonCrudDeleteGuard(new InMemoryPersonUserLinkRepositoryWithLink(personId));

        var error = await guard.CanDeleteAsync(PersonCrudDescriptorFactory.Create(), personId.ToString("D"));

        Assert.NotNull(error);
        Assert.Equal("Organization.Person.DeleteBlockedByUserLink", error!.Code);
    }

    [Fact]
    public async Task GetListAsync_ProjectsRequiredFields()
    {
        dynamic service = BuildService();
        var created = await service.CreateAsync(new PersonDto
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId.ToString("D"),
            DepartmentId = DepartmentId.ToString("D"),
            EmploymentStatus = EmploymentStatus.FullTime,
            IsEnabled = true
        });

        var rows = await service.GetListAsync(
            keyword: "张",
            companyId: CompanyId,
            departmentId: DepartmentId,
            employmentStatus: EmploymentStatus.FullTime);

        var row = ((IEnumerable)rows).Cast<object>().Single();

        Assert.Equal("P001", row.GetType().GetProperty("PersonNo")!.GetValue(row));
        Assert.Equal("张三", row.GetType().GetProperty("Name")!.GetValue(row));
        Assert.Equal("ICP研发中心", row.GetType().GetProperty("CompanyName")!.GetValue(row));
        Assert.Equal("人力资源部", row.GetType().GetProperty("DepartmentName")!.GetValue(row));
        Assert.Equal(EmploymentStatus.FullTime, row.GetType().GetProperty("EmploymentStatus")!.GetValue(row));
        Assert.True((bool)row.GetType().GetProperty("IsEnabled")!.GetValue(row)!);
    }

    private static IPersonService BuildService() =>
        (IPersonService)Activator.CreateInstance(
            typeof(PersonService),
            new object[]
            {
                new InMemoryPersonRepository(),
                new InMemoryPersonUserLinkRepository(),
                new InMemoryCompanyRepository(),
                new InMemoryDepartmentRepository(),
                new FakeIdentityUserDirectory(),
                new FakeAuthorizationSummaryReader()
            })!;

    private static Assembly GetOrganizationAssembly() => typeof(PersonDto).Assembly;

    private sealed class InMemoryPersonRepository : IPersonRepository
    {
        private readonly List<Person> _persons = [];

        public Task<Person?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.FirstOrDefault(person => person.Id == personId) is { } person ? Clone(person) : null);

        public Task<IReadOnlyList<Person>> GetListAsync(
            string? keyword,
            Guid? companyId,
            Guid? departmentId,
            EmploymentStatus? employmentStatus,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Person> query = _persons;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(person =>
                    person.PersonNo.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    person.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            if (companyId.HasValue)
            {
                query = query.Where(person => person.CompanyId == companyId.Value);
            }

            if (departmentId.HasValue)
            {
                query = query.Where(person => person.DepartmentId == departmentId.Value);
            }

            if (employmentStatus.HasValue)
            {
                query = query.Where(person => person.EmploymentStatus == employmentStatus.Value);
            }

            return Task.FromResult<IReadOnlyList<Person>>(query.Select(Clone).ToList());
        }

        public Task<bool> ExistsPersonNoAsync(
            Guid? companyId,
            string personNo,
            Guid? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.Any(person =>
                person.CompanyId == companyId &&
                person.Id != excludedPersonId &&
                string.Equals(person.PersonNo, personNo, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(Person person, CancellationToken cancellationToken = default)
        {
            _persons.Add(Clone(person));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
        {
            var index = _persons.FindIndex(existing => existing.Id == person.Id);
            if (index >= 0)
            {
                _persons[index] = Clone(person);
            }

            return Task.CompletedTask;
        }

        private static Person Clone(Person person) => new()
        {
            Id = person.Id,
            PersonNo = person.PersonNo,
            Name = person.Name,
            CompanyId = person.CompanyId,
            DepartmentId = person.DepartmentId,
            PositionId = person.PositionId,
            EmploymentStatus = person.EmploymentStatus,
            IsEnabled = person.IsEnabled,
            CreatedAt = person.CreatedAt,
            UpdatedAt = person.UpdatedAt
        };
    }

    private sealed class InMemoryPersonUserLinkRepository : IPersonUserLinkRepository
    {
        private readonly List<PersonUserLink> _links = [];

        public Task<PersonUserLink?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonUserLink?>(null);

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            Guid? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
        {
            _links.Add(link);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryPersonUserLinkRepositoryWithLink : IPersonUserLinkRepository
    {
        private readonly Guid _personId;

        public InMemoryPersonUserLinkRepositoryWithLink(Guid personId)
        {
            _personId = personId;
        }

        public Task<PersonUserLink?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonUserLink?>(personId == _personId
                ? new PersonUserLink
                {
                    PersonId = personId,
                    UserId = "user-1",
                    LinkedAt = DateTime.UtcNow,
                    LinkedBy = "test"
                }
                : null);

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            Guid? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies =
        [
            new Company
            {
                Id = CompanyId,
                Code = "ICP",
                Name = "ICP研发中心",
                IsEnabled = true,
                SortOrder = 1
            }
        ];

        public Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.Select(Clone).ToList());

        public Task<Company?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.FirstOrDefault(company => company.Id == companyId) is { } company ? Clone(company) : null);

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.FirstOrDefault(company => string.Equals(company.Code, code, StringComparison.OrdinalIgnoreCase)) is { } company ? Clone(company) : null);

        public Task CreateAsync(Company company, CancellationToken cancellationToken = default)
        {
            _companies.Add(Clone(company));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Company company, CancellationToken cancellationToken = default)
        {
            var index = _companies.FindIndex(existing => existing.Id == company.Id);
            if (index >= 0)
            {
                _companies[index] = Clone(company);
            }

            return Task.CompletedTask;
        }

        private static Company Clone(Company company) => new()
        {
            Id = company.Id,
            Code = company.Code,
            Name = company.Name,
            IsEnabled = company.IsEnabled,
            SortOrder = company.SortOrder,
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        };
    }

    private sealed class InMemoryDepartmentRepository : IDepartmentRepository
    {
        private readonly List<Department> _departments =
        [
            new Department
            {
                Id = DepartmentId,
                CompanyId = CompanyId,
                Code = "HR",
                Name = "人力资源部",
                IsEnabled = true,
                SortOrder = 1
            }
        ];

        public Task<List<Department>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.Where(department => department.CompanyId == companyId).Select(Clone).ToList());

        public Task<Department?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.FirstOrDefault(department => department.Id == departmentId) is { } department ? Clone(department) : null);

        public Task<bool> ExistsSiblingCodeAsync(
            Guid companyId,
            Guid? parentDepartmentId,
            string code,
            Guid? excludedDepartmentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

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

    private sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(
            string keyword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>([]);
    }

    private sealed class FakeAuthorizationSummaryReader : IAuthorizationSummaryReader
    {
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
