using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonAuthorizationSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IOrganizationQuery _query;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;

    // 组织绑定来自稳定查询，授权事实仍由 Authorization 拥有者提供。
    public PersonAuthorizationSummaryProvider(
        IOrganizationQuery query,
        IAuthorizationSummaryReader authorizationSummaryReader)
    {
        _query = query;
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
        var person = (await _query.ReadAsync(cancellationToken)).Find(OrganizationRecordKind.Person, personId);
        var accountId = person?.GetValueOrDefault("AccountEID") as string;
        if (accountId is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        var roles = await _authorizationSummaryReader.GetRoleNamesAsync(accountId, cancellationToken);
        return new StandardCrudSummaryBlock { Status = "ready", Data = roles };
    }
}
