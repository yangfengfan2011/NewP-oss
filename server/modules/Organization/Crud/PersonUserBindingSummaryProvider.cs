using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonUserBindingSummaryProvider(
    IOrganizationQuery query,
    IIdentityAccountDirectory accountDirectory) : IStandardCrudSummaryProvider
{
    public string ResourceKey => "organization.person";
    public string Key => "userBinding";

    /// <inheritdoc/>
    public async Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default)
    {
        var personEid = detail[descriptor.IdField]?.ToString() ?? string.Empty;
        var person = (await query.ReadAsync(cancellationToken)).Find(OrganizationRecordKind.Person, personEid);
        if (person is null)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }

        var accountId = person.GetValueOrDefault("AccountEID") as string;
        IdentityAccountSnapshot? account = null;
        var accountAvailable = true;
        if (accountId is not null)
        {
            try
            {
                account = await accountDirectory.FindAsync(accountId, cancellationToken);
                accountAvailable = account is not null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Person 自有的永久绑定事实仍可降级展示；Identity 展示信息明确标记不可用。
                accountAvailable = false;
            }
        }

        return new StandardCrudSummaryBlock
        {
            Status = "ready",
            Data = new PersonUserBindingSummaryDto(
                (string)person["EID"]!,
                (string)person["ENAME"]!,
                person["IsEnabled"] is true,
                accountId,
                person.GetValueOrDefault("AccountBoundAt") as DateTime?,
                person.GetValueOrDefault("AccountBoundBy") as string,
                person.GetValueOrDefault("AccountBindingReason") as string,
                account is null
                    ? null
                    : new PersonUserBindingAccountDto(
                        account.AccountEid,
                        account.ECODE,
                        account.ENAME,
                        account.Status.ToString(),
                        account.Revision),
                accountAvailable)
        };
    }
}
