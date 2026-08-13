using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonAuthorizationSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IPersonUserBindingRepository _bindingRepository;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;

    public PersonAuthorizationSummaryProvider(
        IPersonUserBindingRepository bindingRepository,
        IAuthorizationSummaryReader authorizationSummaryReader)
    {
        _bindingRepository = bindingRepository;
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
        var person = await _bindingRepository.FindAsync(personId, cancellationToken);
        if (person?.AccountEid is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        var roles = await _authorizationSummaryReader.GetRoleNamesAsync(person.AccountEid, cancellationToken);
        return new StandardCrudSummaryBlock { Status = "ready", Data = roles };
    }
}
