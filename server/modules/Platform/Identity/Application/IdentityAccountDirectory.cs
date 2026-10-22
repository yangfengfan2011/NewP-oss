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

    /// <summary>
    /// 批量读取可公开引用的账号快照；保持输入首次出现顺序并过滤空值、重复值和已删除账号。
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
        IReadOnlyCollection<string> accountEids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountEids);
        var requestedIds = accountEids
            .Where(static accountEid => !string.IsNullOrWhiteSpace(accountEid))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedIds.Length == 0)
        {
            return new Dictionary<string, IdentityAccountSnapshot>(StringComparer.Ordinal);
        }

        var accounts = await _repository.FindAccountsByIdsAsync(requestedIds, cancellationToken);
        var snapshots = new Dictionary<string, IdentityAccountSnapshot>(StringComparer.Ordinal);
        foreach (var accountEid in requestedIds)
        {
            if (accounts.TryGetValue(accountEid, out var account)
                && account.Status != IdentityAccountStatus.Deleted)
            {
                snapshots[accountEid] = IdentityAccountApplicationService.ToSnapshot(account);
            }
        }

        return snapshots;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
        IdentityAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var accounts = await _repository.SearchAccountsAsync(search, cancellationToken);
        return accounts
            .Select(IdentityAccountApplicationService.ToSnapshot)
            .ToArray();
    }
}
