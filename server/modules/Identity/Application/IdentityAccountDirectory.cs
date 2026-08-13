using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Repositories;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class IdentityAccountDirectory : IIdentityAccountDirectory
{
    private readonly IIdentityRepository _repository;

    public IdentityAccountDirectory(IIdentityRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<IdentityAccountSnapshot?> FindAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountEid))
        {
            return null;
        }

        var account = await _repository.FindAccountAsync(accountEid, cancellationToken);
        return account is null || account.Status == IdentityAccountStatus.Deleted
            ? null
            : IdentityAccountApplicationService.ToSnapshot(account);
    }

    public async ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
        IdentityAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var limit = Math.Clamp(search.Limit, 1, 100);
        var query = search.Query?.Trim();
        var accounts = await _repository.SearchAccountsAsync(cancellationToken);

        return accounts
            .Where(account => search.Status is null
                ? account.Status != IdentityAccountStatus.Deleted
                : account.Status == search.Status)
            .Where(account => search.IsPersonnelBound is null || account.IsPersonnelBound == search.IsPersonnelBound)
            .Where(account => string.IsNullOrWhiteSpace(query)
                || account.ECODE.Contains(query, StringComparison.OrdinalIgnoreCase)
                || account.ENAME.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(account => account.ECODE, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(IdentityAccountApplicationService.ToSnapshot)
            .ToArray();
    }
}
