using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonUserLinkService : IPersonUserLinkService
{
    private const string PersonResourceKey = "organization.person";
    private readonly IPersonUserLinkRepository _personUserLinkRepository;
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly IStandardCrudResourceRegistry _crudResourceRegistry;
    private readonly IStandardCrudService _standardCrudService;

    public PersonUserLinkService(
        IPersonUserLinkRepository personUserLinkRepository,
        IIdentityUserDirectory identityUserDirectory,
        IPlatformObjectIdGenerator objectIdGenerator,
        IStandardCrudResourceRegistry crudResourceRegistry,
        IStandardCrudService standardCrudService)
    {
        _personUserLinkRepository = personUserLinkRepository;
        _identityUserDirectory = identityUserDirectory;
        _objectIdGenerator = objectIdGenerator;
        _crudResourceRegistry = crudResourceRegistry;
        _standardCrudService = standardCrudService;
    }

    public async Task<PersonUserLinkDto> LinkUserAsync(
        string personId,
        LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new InvalidOperationException("绑定账号不能为空。");
        }

        await EnsurePersonExistsAsync(personId, cancellationToken);

        if (await _personUserLinkRepository.ExistsByUserIdAsync(request.UserId, personId, cancellationToken))
        {
            throw new InvalidOperationException("该账号已绑定其他人员。");
        }

        // 绑定前通过身份目录确认用户存在，避免组织模块保存悬空账号引用。
        await _identityUserDirectory.EnsureUserExistsAsync(request.UserId, cancellationToken);

        var existingLink = await _personUserLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        var link = new PersonUserLink
        {
            Id = string.IsNullOrWhiteSpace(existingLink?.Id)
                ? _objectIdGenerator.NewId(nameof(PersonUserLink))
                : existingLink.Id,
            PersonId = personId,
            UserId = request.UserId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = string.IsNullOrWhiteSpace(request.LinkedBy) ? "system" : request.LinkedBy
        };

        await _personUserLinkRepository.UpsertAsync(link, cancellationToken);
        return MapToDto(link);
    }

    private async Task EnsurePersonExistsAsync(string personId, CancellationToken cancellationToken)
    {
        var descriptor = _crudResourceRegistry.FindByResourceKey(PersonResourceKey)
            ?? throw new InvalidOperationException("人员标准 CRUD 资源未注册。");

        var detail = await _standardCrudService.GetDetailAsync(descriptor, personId, cancellationToken);
        if (detail is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }
    }

    private static PersonUserLinkDto MapToDto(PersonUserLink link) => new()
    {
        Id = link.Id,
        PersonId = link.PersonId,
        UserId = link.UserId,
        CreatedAt = link.LinkedAt,
        LinkedAt = link.LinkedAt,
        LinkedBy = string.IsNullOrWhiteSpace(link.LinkedBy) ? "system" : link.LinkedBy
    };
}
