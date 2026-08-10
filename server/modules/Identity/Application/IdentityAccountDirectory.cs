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
        string userEid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEid))
        {
            return null;
        }

        var user = await _repository.FindUserAsync(userEid, cancellationToken);
        return user is null || user.Status == IdentityAccountStatus.Deleted
            ? null
            : IdentityAccountApplicationService.ToSnapshot(user);
    }

    public async ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
        IdentityAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var limit = Math.Clamp(search.Limit, 1, 100);
        var query = search.Query?.Trim();
        var users = await _repository.SearchUsersAsync(cancellationToken);

        return users
            .Where(user => search.Status is null
                ? user.Status != IdentityAccountStatus.Deleted
                : user.Status == search.Status)
            .Where(user => search.IsPersonnelBound is null || user.IsPersonnelBound == search.IsPersonnelBound)
            .Where(user => string.IsNullOrWhiteSpace(query)
                || user.ECODE.Contains(query, StringComparison.OrdinalIgnoreCase)
                || user.ENAME.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(user => user.ECODE, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(IdentityAccountApplicationService.ToSnapshot)
            .ToArray();
    }
}
