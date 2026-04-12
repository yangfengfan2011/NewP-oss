using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonService : IPersonService
{
    private readonly IPersonRepository _personRepository;
    private readonly IPersonUserLinkRepository _personUserLinkRepository;
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;

    public PersonService(
        IPersonRepository personRepository,
        IPersonUserLinkRepository personUserLinkRepository,
        IIdentityUserDirectory identityUserDirectory,
        IAuthorizationSummaryReader authorizationSummaryReader)
    {
        _personRepository = personRepository;
        _personUserLinkRepository = personUserLinkRepository;
        _identityUserDirectory = identityUserDirectory;
        _authorizationSummaryReader = authorizationSummaryReader;
    }

    public async Task<PersonDto?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        return person is null ? null : MapToDto(person);
    }

    public async Task<PersonDetailDto?> GetDetailAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        var link = await _personUserLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        var authorizationSummary = await GetAuthorizationSummaryAsync(link, cancellationToken);

        return new PersonDetailDto
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
            UserLink = link is null ? null : MapToDto(link),
            AuthorizationSummary = authorizationSummary
        };
    }

    public async Task<PersonDto> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePersonNoAvailableAsync(request.CompanyId, request.PersonNo, null, cancellationToken);

        var person = new Person
        {
            PersonNo = request.PersonNo,
            Name = request.Name,
            CompanyId = request.CompanyId,
            DepartmentId = request.DepartmentId,
            PositionId = request.PositionId,
            EmploymentStatus = request.EmploymentStatus,
            IsEnabled = request.IsEnabled
        };

        await _personRepository.CreateAsync(person, cancellationToken);
        return MapToDto(person);
    }

    public async Task<PersonDto> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }

        await EnsurePersonNoAvailableAsync(request.CompanyId, person.PersonNo, personId, cancellationToken);

        person.CompanyId = request.CompanyId;
        person.DepartmentId = request.DepartmentId;
        person.PositionId = request.PositionId;
        person.Name = request.Name;
        person.EmploymentStatus = request.EmploymentStatus;
        person.IsEnabled = request.IsEnabled;
        person.UpdatedAt = DateTime.UtcNow;

        await _personRepository.UpdateAsync(person, cancellationToken);
        return MapToDto(person);
    }

    public async Task<PersonUserLinkDto> LinkUserAsync(
        Guid personId,
        LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }

        if (await _personUserLinkRepository.ExistsByUserIdAsync(request.UserId, personId, cancellationToken))
        {
            throw new InvalidOperationException("该账号已绑定其他人员。");
        }

        await _identityUserDirectory.EnsureUserExistsAsync(request.UserId, cancellationToken);

        var link = new PersonUserLink
        {
            PersonId = personId,
            UserId = request.UserId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = string.IsNullOrWhiteSpace(request.LinkedBy) ? "system" : request.LinkedBy
        };

        await _personUserLinkRepository.UpsertAsync(link, cancellationToken);
        return MapToDto(link);
    }

    private async Task<IReadOnlyList<string>> GetAuthorizationSummaryAsync(
        PersonUserLink? link,
        CancellationToken cancellationToken)
    {
        if (link is null)
        {
            return [];
        }

        try
        {
            return await _authorizationSummaryReader.GetRoleNamesAsync(link.UserId, cancellationToken);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private async Task EnsurePersonNoAvailableAsync(
        Guid companyId,
        string personNo,
        Guid? excludedPersonId,
        CancellationToken cancellationToken)
    {
        if (await _personRepository.ExistsPersonNoAsync(companyId, personNo, excludedPersonId, cancellationToken))
        {
            throw new InvalidOperationException("同一公司下人员工号必须唯一。");
        }
    }

    private static PersonDto MapToDto(Person person) => new()
    {
        Id = person.Id,
        PersonNo = person.PersonNo,
        Name = person.Name,
        CompanyId = person.CompanyId,
        DepartmentId = person.DepartmentId,
        PositionId = person.PositionId,
        EmploymentStatus = person.EmploymentStatus,
        IsEnabled = person.IsEnabled,
        CreatedAt = person.CreatedAt
    };

    private static PersonUserLinkDto MapToDto(PersonUserLink link) => new()
    {
        PersonId = link.PersonId,
        UserId = link.UserId,
        LinkedAt = link.LinkedAt,
        LinkedBy = link.LinkedBy
    };
}
