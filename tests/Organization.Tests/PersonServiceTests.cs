using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class PersonServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyId2 = Guid.Parse("11111111-1111-1111-1111-222222222222");
    private static readonly Guid DepartmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PositionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task CreatePerson_WithRequiredFields_CreatesPerson()
    {
        var service = BuildService();

        var result = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        Assert.Equal("P001", result.PersonNo);
        Assert.Equal("张三", result.Name);
        Assert.Equal(CompanyId, result.CompanyId);
        Assert.Equal(DepartmentId, result.DepartmentId);
        Assert.Null(result.PositionId);
        Assert.Equal(EmploymentStatus.FullTime, result.EmploymentStatus);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public async Task UpdatePerson_ValidRequest_UpdatesFields()
    {
        var service = BuildService();
        var created = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        var updated = await service.UpdateAsync(created.Id, new UpdatePersonRequest
        {
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            PositionId = PositionId,
            Name = "张三（更新）",
            EmploymentStatus = EmploymentStatus.Contractor,
            IsEnabled = false
        });

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("P001", updated.PersonNo);
        Assert.Equal("张三（更新）", updated.Name);
        Assert.Equal(PositionId, updated.PositionId);
        Assert.Equal(EmploymentStatus.Contractor, updated.EmploymentStatus);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task CreatePerson_WhenPersonNoAlreadyExistsInCompany_ThrowsInvalidOperationException()
    {
        var service = BuildService();
        await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreatePersonRequest
            {
                PersonNo = "P001",
                Name = "李四",
                CompanyId = CompanyId,
                DepartmentId = DepartmentId
            }));
    }

    [Fact]
    public async Task UpdatePerson_WhenPersonNoAlreadyExistsInCompany_ThrowsInvalidOperationException()
    {
        var service = BuildService();
        var personA = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });
        var personB = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "李四",
            CompanyId = CompanyId2,
            DepartmentId = DepartmentId
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(personB.Id, new UpdatePersonRequest
            {
                CompanyId = CompanyId,
                DepartmentId = DepartmentId,
                PositionId = personB.PositionId,
                Name = "李四（更新）",
                EmploymentStatus = personB.EmploymentStatus,
                IsEnabled = personB.IsEnabled
            }));
    }

    [Fact]
    public async Task LinkUser_WhenUserAlreadyBound_ThrowsInvalidOperationException()
    {
        var service = BuildService();
        var personA = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });
        var personB = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P002",
            Name = "李四",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await service.LinkUserAsync(personA.Id, new LinkPersonUserRequest
        {
            UserId = "user-001",
            LinkedBy = "tester"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkUserAsync(personB.Id, new LinkPersonUserRequest
            {
                UserId = "user-001",
                LinkedBy = "tester"
            }));
    }

    [Fact]
    public async Task LinkUser_EnsuresUserExistsBeforePersistingLink()
    {
        var operations = new List<string>();
        var identityUserDirectory = new FakeIdentityUserDirectory(operations);
        var service = BuildService(
            new InMemoryPersonRepository(),
            new InMemoryPersonUserLinkRepository(operations),
            identityUserDirectory,
            new FakeAuthorizationSummaryReader());
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        var result = await service.LinkUserAsync(person.Id, new LinkPersonUserRequest
        {
            UserId = "user-002",
            LinkedBy = "tester"
        });

        Assert.Equal("user-002", result.UserId);
        Assert.Equal(
            ["ensure:user-002", "upsert:user-002"],
            operations);
    }

    [Fact]
    public async Task LinkUser_WithNullIdentityUserDirectory_ThrowsInvalidOperationException()
    {
        var service = BuildService(
            new InMemoryPersonRepository(),
            new InMemoryPersonUserLinkRepository(),
            new NullIdentityUserDirectory(),
            new FakeAuthorizationSummaryReader());
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P004",
            Name = "赵六",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkUserAsync(person.Id, new LinkPersonUserRequest
            {
                UserId = "user-null"
            }));
    }

    [Fact]
    public async Task NullIdentityUserDirectory_SearchAsync_ReturnsEmptyOptions()
    {
        var directory = new NullIdentityUserDirectory();

        var result = await directory.SearchAsync("admin");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDetail_WhenPersonLinked_ReturnsUserLink()
    {
        var service = BuildService();
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P003",
            Name = "王五",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await service.LinkUserAsync(person.Id, new LinkPersonUserRequest
        {
            UserId = "user-003",
            LinkedBy = "tester"
        });

        var detail = await service.GetDetailAsync(person.Id);

        Assert.NotNull(detail);
        Assert.Equal(person.Id, detail!.Id);
        Assert.NotNull(detail.UserLink);
        Assert.Equal("user-003", detail.UserLink!.UserId);
    }

    [Fact]
    public async Task GetPersonDetail_WhenUserBound_ReturnsAuthorizationSummary()
    {
        var service = BuildService(
            new InMemoryPersonRepository(),
            new InMemoryPersonUserLinkRepository(),
            new FakeIdentityUserDirectory(),
            new FakeAuthorizationSummaryReader(["平台管理员"]));
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P100",
            Name = "王五",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await service.LinkUserAsync(person.Id, new LinkPersonUserRequest
        {
            UserId = "admin",
            LinkedBy = "tester"
        });

        var detail = await service.GetDetailAsync(person.Id);

        Assert.NotNull(detail);
        Assert.Contains("平台管理员", detail!.AuthorizationSummary);
    }

    [Fact]
    public async Task GetPersonDetail_WhenAuthorizationSummaryReaderFails_ReturnsEmptyAuthorizationSummary()
    {
        var service = BuildService(
            new InMemoryPersonRepository(),
            new InMemoryPersonUserLinkRepository(),
            new FakeIdentityUserDirectory(),
            new ThrowingAuthorizationSummaryReader());
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            PersonNo = "P101",
            Name = "赵七",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId
        });

        await service.LinkUserAsync(person.Id, new LinkPersonUserRequest
        {
            UserId = "ops-user",
            LinkedBy = "tester"
        });

        var detail = await service.GetDetailAsync(person.Id);

        Assert.NotNull(detail);
        Assert.Empty(detail!.AuthorizationSummary);
    }

    private static IPersonService BuildService() =>
        BuildService(
            new InMemoryPersonRepository(),
            new InMemoryPersonUserLinkRepository(),
            new FakeIdentityUserDirectory(),
            new FakeAuthorizationSummaryReader());

    private static IPersonService BuildService(
        IPersonRepository personRepository,
        IPersonUserLinkRepository personUserLinkRepository,
        IIdentityUserDirectory identityUserDirectory,
        IAuthorizationSummaryReader authorizationSummaryReader) =>
        new PersonService(
            personRepository,
            personUserLinkRepository,
            identityUserDirectory,
            authorizationSummaryReader);

    private sealed class InMemoryPersonRepository : IPersonRepository
    {
        private readonly List<Person> _persons = [];

        public Task<Person?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.FirstOrDefault(person => person.Id == personId) is { } person ? Clone(person) : null);

        public Task<bool> ExistsPersonNoAsync(
            Guid companyId,
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
        private readonly List<string>? _operations;

        public InMemoryPersonUserLinkRepository(List<string>? operations = null)
        {
            _operations = operations;
        }

        public Task<PersonUserLink?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_links.FirstOrDefault(link => link.PersonId == personId) is { } link ? Clone(link) : null);

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            Guid? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_links.Any(link =>
                string.Equals(link.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                link.PersonId != excludedPersonId));

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
        {
            _operations?.Add($"upsert:{link.UserId}");

            var index = _links.FindIndex(existing => existing.PersonId == link.PersonId);
            if (index >= 0)
            {
                _links[index] = Clone(link);
            }
            else
            {
                _links.Add(Clone(link));
            }

            return Task.CompletedTask;
        }

        private static PersonUserLink Clone(PersonUserLink link) => new()
        {
            PersonId = link.PersonId,
            UserId = link.UserId,
            LinkedAt = link.LinkedAt,
            LinkedBy = link.LinkedBy
        };
    }

    private sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
    {
        private readonly List<string>? _operations;

        public FakeIdentityUserDirectory(List<string>? operations = null)
        {
            _operations = operations;
        }

        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default)
        {
            _operations?.Add($"ensure:{userId}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(
            string keyword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>(
            [
                new IdentityUserOptionDto
                {
                    UserId = "user-001",
                    DisplayName = "张三"
                }
            ]);
    }

    private sealed class FakeAuthorizationSummaryReader : IAuthorizationSummaryReader
    {
        private readonly IReadOnlyList<string> _roleNames;

        public FakeAuthorizationSummaryReader(IReadOnlyList<string>? roleNames = null)
        {
            _roleNames = roleNames ?? [];
        }

        public Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roleNames);
    }

    private sealed class ThrowingAuthorizationSummaryReader : IAuthorizationSummaryReader
    {
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new Exception("authorization unavailable");
    }
}
