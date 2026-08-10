using ICP.Platform.Modules.Identity.Models;

namespace ICP.Platform.Modules.Identity.Repositories;

public interface IIdentityRepository
{
    ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default);

    ValueTask<User?> FindUserAsync(string userEid, CancellationToken cancellationToken = default);

    ValueTask<User?> FindUserByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<User>> SearchUsersAsync(CancellationToken cancellationToken = default);

    ValueTask<LocalCredential?> FindCredentialAsync(string userEid, CancellationToken cancellationToken = default);

    void AddUser(User user);

    void AddCredential(LocalCredential credential);

    ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default);

    ValueTask RemoveCredentialAsync(string userEid, CancellationToken cancellationToken = default);

    void AddSecurityEvent(IdentitySecurityEvent securityEvent);

    ValueTask<bool> TryReplaceUserAsync(User user, long expectedRevision, CancellationToken cancellationToken = default);
}
