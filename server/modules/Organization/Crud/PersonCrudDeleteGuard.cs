using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDeleteGuard : IStandardCrudDeleteGuard
{
    private readonly IPersonUserLinkRepository _userLinkRepository;

    public PersonCrudDeleteGuard(IPersonUserLinkRepository userLinkRepository)
    {
        _userLinkRepository = userLinkRepository;
    }

    public string ResourceKey => "organization.person";

    /// <summary>
    /// 条件删除的人员边界：已绑定账号时必须稳定拒绝，避免标准删除绕过 user-link 特殊入口。
    /// </summary>
    public async Task<StandardCrudError?> CanDeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var personId))
        {
            return new StandardCrudError
            {
                StatusCode = 400,
                Code = "Organization.Person.InvalidId",
                Message = "人员标识必须是有效 GUID。",
                ReasonKey = "organization.person.invalidId"
            };
        }

        var link = await _userLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        return link is null
            ? null
            : new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserLink",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserLink"
            };
    }
}
