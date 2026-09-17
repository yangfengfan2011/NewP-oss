using ICP.Platform.Modules.Authentication.Models;

namespace ICP.Platform.Modules.Authentication.Repositories;

public interface IAuthenticationSessionRepository
{
    void Add(AuthenticationSession session);

    /// <summary>按服务端验证的会话标识读取当前状态；只供轮换事务重新核验。</summary>
    ValueTask<AuthenticationSession?> FindByIdAsync(string sessionEid, CancellationToken cancellationToken = default);

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

    /// <summary>在同一事务内批量撤销目标账号会话，不自行保存或提交。</summary>
    ValueTask<int> RevokeAccountsAsync(IReadOnlyCollection<string> accountIds, DateTime revokedAtUtc,
        string reason, string actorId, CancellationToken cancellationToken = default);

    ValueTask<int> PurgeAsync(
        DateTime retentionCutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}
