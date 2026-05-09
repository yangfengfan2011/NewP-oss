using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonAuthorizationSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IPersonUserLinkRepository _userLinkRepository;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;

    public PersonAuthorizationSummaryProvider(
        IPersonUserLinkRepository userLinkRepository,
        IAuthorizationSummaryReader authorizationSummaryReader)
    {
        _userLinkRepository = userLinkRepository;
        _authorizationSummaryReader = authorizationSummaryReader;
    }

    public string ResourceKey => "organization.person";
    public string Key => "authorizationSummary";

    /// <summary>
    /// 仅在人员已绑定账号时读取授权摘要；账号绑定仍是 Organization 模块内的受控入口。
    /// </summary>
    public async Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default)
    {
        var personId = detail[descriptor.IdField]?.ToString() ?? string.Empty;
        var link = await _userLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        if (link is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        var roles = await _authorizationSummaryReader.GetRoleNamesAsync(link.UserId, cancellationToken);
        return new StandardCrudSummaryBlock { Status = "ready", Data = roles };
    }
}
