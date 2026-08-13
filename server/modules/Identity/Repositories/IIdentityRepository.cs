using ICP.Platform.Modules.Identity.Models;

namespace ICP.Platform.Modules.Identity.Repositories;

public interface IIdentityRepository
{
    ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default);

    ValueTask<IdentityAccount?> FindAccountAsync(string accountEid, CancellationToken cancellationToken = default);

    ValueTask<IdentityAccount?> FindAccountByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<IdentityAccount>> SearchAccountsAsync(CancellationToken cancellationToken = default);

    ValueTask<LocalCredential?> FindCredentialAsync(string accountEid, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(
        string targetAccountEid,
        CancellationToken cancellationToken = default);

    void AddAccount(IdentityAccount account);

    void AddCredential(LocalCredential credential);

    ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default);

    ValueTask RemoveCredentialAsync(string accountEid, CancellationToken cancellationToken = default);

    void AddSecurityEvent(IdentitySecurityEvent securityEvent);

    ValueTask<bool> TryReplaceAccountAsync(IdentityAccount account, long expectedRevision, CancellationToken cancellationToken = default);
}
