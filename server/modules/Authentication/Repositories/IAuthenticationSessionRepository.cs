using ICP.Platform.Modules.Authentication.Models;

namespace ICP.Platform.Modules.Authentication.Repositories;

public interface IAuthenticationSessionRepository
{
    void Add(AuthenticationSession session);

    ValueTask<AuthenticationSession?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RevokeCurrentAsync(
        string sessionEid,
        DateTime revokedAtUtc,
        string reason,
        string actorId,
        CancellationToken cancellationToken = default);

    ValueTask<int> RevokeAllAsync(
        string accountEid,
        DateTime revokedAtUtc,
        string reason,
        string actorId,
        CancellationToken cancellationToken = default);

    ValueTask<int> PurgeAsync(
        DateTime retentionCutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}
