using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonUserBindingSummaryProvider(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory) : IStandardCrudSummaryProvider
{
    public string ResourceKey => "organization.person";
    public string Key => "userBinding";

    public async Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default)
    {
        var personEid = detail[descriptor.IdField]?.ToString() ?? string.Empty;
        var person = await repository.FindAsync(personEid, cancellationToken);
        if (person is null)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }

        IdentityAccountSnapshot? account = null;
        var accountAvailable = true;
        if (person.UserEid is not null)
        {
            try
            {
                account = await accountDirectory.FindAsync(person.UserEid, cancellationToken);
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
                person.PersonEid,
                person.PersonName,
                person.IsEnabled,
                person.UserEid,
                person.BoundAt,
                person.BoundBy,
                person.BindingReason,
                account is null
                    ? null
                    : new PersonUserBindingAccountDto(
                        account.UserEid,
                        account.ECODE,
                        account.ENAME,
                        account.Status.ToString(),
                        account.Revision),
                accountAvailable)
        };
    }
}
