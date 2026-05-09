using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonService : IPersonService
{
    private readonly IPersonRepository _personRepository;
    private readonly IPersonUserLinkRepository _personUserLinkRepository;
    private readonly ICompanyRepository? _companyRepository;
    private readonly IDepartmentRepository? _departmentRepository;
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public PersonService(
        IPersonRepository personRepository,
        IPersonUserLinkRepository personUserLinkRepository,
        IIdentityUserDirectory identityUserDirectory,
        IAuthorizationSummaryReader authorizationSummaryReader,
        IPlatformObjectIdGenerator objectIdGenerator)
        : this(personRepository, personUserLinkRepository, null, null, identityUserDirectory, authorizationSummaryReader, objectIdGenerator)
    {
    }

    public PersonService(
        IPersonRepository personRepository,
        IPersonUserLinkRepository personUserLinkRepository,
        ICompanyRepository? companyRepository,
        IDepartmentRepository? departmentRepository,
        IIdentityUserDirectory identityUserDirectory,
        IAuthorizationSummaryReader authorizationSummaryReader,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _personRepository = personRepository;
        _personUserLinkRepository = personUserLinkRepository;
        _companyRepository = companyRepository;
        _departmentRepository = departmentRepository;
        _identityUserDirectory = identityUserDirectory;
        _authorizationSummaryReader = authorizationSummaryReader;
        _objectIdGenerator = objectIdGenerator;
    }

    public async Task<PersonDto?> GetByIdAsync(string personId, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        return person is null ? null : MapToDto(person);
    }

    public async Task<PersonDetailDto?> GetDetailAsync(string personId, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        PersonUserLink? link;
        var userLinkDependencyUnavailable = false;
        try
        {
            link = await _personUserLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        }
        catch (Exception)
        {
            // 绑定摘要是只读承接，依赖异常时不阻断人员主体详情，只记录为 dependencyUnavailable。
            link = null;
            userLinkDependencyUnavailable = true;
        }

        // 摘要块是只读承接，不应反向影响人员主体详情的可读性。
        var userLinkSummary = BuildUserLinkSummary(link, userLinkDependencyUnavailable);
        var authorizationSummary = await BuildAuthorizationSummaryAsync(link, userLinkDependencyUnavailable, cancellationToken);

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
            UserLinkSummary = userLinkSummary,
            AuthorizationSummary = authorizationSummary
        };
    }

    public async Task<IReadOnlyList<PersonListItemDto>> GetListAsync(
        string? keyword,
        string? companyId,
        string? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default)
    {
        var persons = await _personRepository.GetListAsync(keyword, companyId, departmentId, employmentStatus, cancellationToken);
        var items = new List<PersonListItemDto>(persons.Count);

        foreach (var person in persons)
        {
            items.Add(new PersonListItemDto
            {
                Id = person.Id,
                PersonNo = person.PersonNo,
                Name = person.Name,
                CompanyName = await ResolveCompanyNameAsync(person.CompanyId, cancellationToken),
                DepartmentName = await ResolveDepartmentNameAsync(person.DepartmentId, cancellationToken),
                EmploymentStatus = person.EmploymentStatus,
                IsEnabled = person.IsEnabled
            });
        }

        return items;
    }

    public async Task<PersonDto> CreateAsync(PersonDto dto, CancellationToken cancellationToken = default)
    {
        var companyId = NormalizeOptionalId(dto.CompanyId);
        var departmentId = NormalizeOptionalId(dto.DepartmentId);
        var positionId = NormalizeOptionalId(dto.PositionId);

        // 人员工号唯一性按“同一公司”收口，而不是全平台全局唯一。
        await EnsurePersonNoAvailableAsync(companyId, dto.PersonNo, null, cancellationToken);

        var person = new Person
        {
            Id = _objectIdGenerator.NewId(nameof(Person)),
            PersonNo = dto.PersonNo,
            Name = dto.DisplayName,
            CompanyId = companyId,
            DepartmentId = departmentId,
            PositionId = positionId,
            EmploymentStatus = dto.EmploymentStatus ?? EmploymentStatus.FullTime,
            IsEnabled = dto.IsEnabled
        };

        await _personRepository.CreateAsync(person, cancellationToken);
        return MapToDto(person);
    }

    public async Task<PersonDto> UpdateAsync(string personId, PersonDto dto, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }

        var companyId = NormalizeOptionalId(dto.CompanyId);
        var departmentId = NormalizeOptionalId(dto.DepartmentId);
        var positionId = NormalizeOptionalId(dto.PositionId);

        await EnsurePersonNoAvailableAsync(companyId, person.PersonNo, personId, cancellationToken);

        // PersonNo 继续保持只读，避免 DTO 合并后把人员工号修改放进同一批行为变更里。
        person.CompanyId = companyId;
        person.DepartmentId = departmentId;
        person.PositionId = positionId;
        person.Name = dto.DisplayName;
        person.EmploymentStatus = dto.EmploymentStatus ?? person.EmploymentStatus;
        person.IsEnabled = dto.IsEnabled;
        person.UpdatedAt = DateTime.UtcNow;

        await _personRepository.UpdateAsync(person, cancellationToken);
        return MapToDto(person);
    }

    public async Task<PersonDetailDto> DisableAsync(string personId, CancellationToken cancellationToken = default)
    {
        var person = await _personRepository.GetByIdAsync(personId, cancellationToken);
        if (person is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }

        // 停用只改变可用性，不改变人员主数据；返回刷新后的详情，方便页面直接接住状态切换结果。
        person.IsEnabled = false;
        person.UpdatedAt = DateTime.UtcNow;
        await _personRepository.UpdateAsync(person, cancellationToken);

        return await GetDetailAsync(personId, cancellationToken)
            ?? throw new InvalidOperationException($"人员 '{personId}' 不存在。");
    }

    public async Task<PersonUserLinkDto> LinkUserAsync(
        string personId,
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

        // 绑定前先通过身份目录确认用户存在，避免组织模块自己持有“悬空用户 ID”。
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

    private static PersonSummaryBlockDto BuildUserLinkSummary(
        PersonUserLink? link,
        bool dependencyUnavailable)
    {
        if (dependencyUnavailable)
        {
            return new PersonSummaryBlockDto { Status = "dependencyUnavailable" };
        }

        if (link is null)
        {
            return new PersonSummaryBlockDto { Status = "notConfigured" };
        }

        return new PersonSummaryBlockDto
        {
            Status = "ready",
            Data = MapToDto(link)
        };
    }

    private async Task<PersonSummaryBlockDto> BuildAuthorizationSummaryAsync(
        PersonUserLink? link,
        bool dependencyUnavailable,
        CancellationToken cancellationToken)
    {
        if (dependencyUnavailable)
        {
            return new PersonSummaryBlockDto { Status = "dependencyUnavailable" };
        }

        if (link is null)
        {
            return new PersonSummaryBlockDto { Status = "notConfigured" };
        }

        try
        {
            var roleNames = await _authorizationSummaryReader.GetRoleNamesAsync(link.UserId, cancellationToken);
            return new PersonSummaryBlockDto
            {
                Status = "ready",
                Data = roleNames
            };
        }
        catch (Exception)
        {
            // 授权摘要是可降级信息；依赖不可用时只降级块状态，不放大为整页失败。
            return new PersonSummaryBlockDto { Status = "dependencyUnavailable" };
        }
    }

    private async Task<string> ResolveCompanyNameAsync(string? companyId, CancellationToken cancellationToken)
    {
        if (_companyRepository is null || string.IsNullOrWhiteSpace(companyId))
        {
            return string.Empty;
        }

        try
        {
            var company = await _companyRepository.GetByIdAsync(companyId, cancellationToken);
            return company?.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private async Task<string> ResolveDepartmentNameAsync(string? departmentId, CancellationToken cancellationToken)
    {
        if (_departmentRepository is null || string.IsNullOrWhiteSpace(departmentId))
        {
            return string.Empty;
        }

        try
        {
            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            return department?.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private async Task EnsurePersonNoAvailableAsync(
        string? companyId,
        string personNo,
        string? excludedPersonId,
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
        DisplayName = person.Name,
        PersonNo = person.PersonNo,
        CompanyId = person.CompanyId,
        DepartmentId = person.DepartmentId,
        PositionId = person.PositionId,
        EmploymentStatus = person.EmploymentStatus,
        IsEnabled = person.IsEnabled,
        CreatedAt = person.CreatedAt,
        UpdatedAt = person.UpdatedAt
    };

    private static PersonUserLinkDto MapToDto(PersonUserLink link) => new()
    {
        Id = link.PersonId.ToString(),
        PersonId = link.PersonId.ToString(),
        UserId = link.UserId,
        CreatedAt = link.LinkedAt,
        LinkedAt = link.LinkedAt,
        LinkedBy = link.LinkedBy
    };

    // 平台对象引用按字符串协议流转；空白值在领域层统一视为未关联。
    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
