using ICP.Platform.Persistence.Transactions;
using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Models;
using ICP.Platform.Modules.Authentication.Repositories;

namespace ICP.Platform.Modules.Authentication.Application;

public sealed class AuthenticationSessionRegistry : IAuthenticationSessionRegistry, IAuthenticationSessionBatchRevoker
{
    private readonly IAuthenticationSessionRepository _repository;
    private readonly IIdentityAccountDirectory _accountDirectory;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly IAuthenticationSessionCredentialGenerator _credentialGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationSessionOptions _options;
    private readonly IIdentityAdmissionPolicy[] _admissionPolicies;

    // 组合会话存储及模块准入规则，认证模块不解释组织事实。
    public AuthenticationSessionRegistry(
        IAuthenticationSessionRepository repository,
        IIdentityAccountDirectory accountDirectory,
        IPlatformObjectIdGenerator objectIdGenerator,
        IAuthenticationSessionCredentialGenerator credentialGenerator,
        TimeProvider timeProvider,
        AuthenticationSessionOptions options,
        IEnumerable<IIdentityAdmissionPolicy>? admissionPolicies = null)
    {
        _repository = repository;
        _accountDirectory = accountDirectory;
        _objectIdGenerator = objectIdGenerator;
        _credentialGenerator = credentialGenerator;
        _timeProvider = timeProvider;
        _options = options;
        _admissionPolicies = admissionPolicies?.ToArray() ?? [];
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsActiveAsync(string sessionEid, string accountEid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionEid) || string.IsNullOrWhiteSpace(accountEid)) return false;
        var session = await _repository.FindByIdAsync(sessionEid, cancellationToken);
        return session is not null && session.AccountEID == accountEid && session.RevokedAt is null
            && _timeProvider.GetUtcNow().UtcDateTime < session.ExpiresAt;
    }

    // 在生成凭据和登记实体之前解析最终会话时区，确保非法输入没有持久化副作用。
    public async ValueTask<AuthenticationSessionIssuance> RegisterAsync(
        AuthenticationSessionRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.AccountEid);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.ActorId);
        if (!Enum.IsDefined(registration.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(registration), "Authentication session type is invalid.");
        }
        if (_options.Lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Authentication session lifetime must be positive.");
        }

        var timeZoneId = AuthenticationSessionTimeZone.Resolve(
            registration.TimeZoneId,
            _options.DefaultTimeZoneId);

        var account = await _accountDirectory.FindAsync(registration.AccountEid, cancellationToken);
        if (account is not { Status: IdentityAccountStatus.Enabled } || account.PersonnelDisableHold)
            throw new AuthenticationSessionException(AuthenticationSessionFailureCodes.AdmissionDenied);
        if (!account.IsLocalAdministrator && _admissionPolicies.Length == 0)
            throw new AuthenticationSessionException(AuthenticationSessionFailureCodes.AdmissionUnavailable);
        foreach (var policy in _admissionPolicies)
            if (!await policy.IsAllowedAsync(account, cancellationToken))
                throw new AuthenticationSessionException(AuthenticationSessionFailureCodes.AdmissionDenied);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var credential = _credentialGenerator.Issue();
        EnsureCredentialIs256Bit(credential);
        var session = new AuthenticationSession
        {
            EID = _objectIdGenerator.NewId(nameof(AuthenticationSession)),
            AccountEID = registration.AccountEid,
            TokenHash = ComputeHash(credential),
            SessionType = registration.Type,
            TimeZoneId = timeZoneId,
            ExpiresAt = now.Add(_options.Lifetime),
            CreatedBy = registration.ActorId,
            CreatedAt = now,
            UpdatedBy = registration.ActorId,
            UpdatedAt = now
        };
        _repository.Add(session);
        return new AuthenticationSessionIssuance
        {
            Credential = credential,
            Session = ToSnapshot(session)
        };
    }

    // 每次验证当前准入；临时不可验证保留凭据，确定失效才拒绝会话。
    public async ValueTask<AuthenticationSessionValidation> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return Result(AuthenticationSessionValidationStatus.Unknown);
        }

        try
        {
            var session = await _repository.FindByTokenHashAsync(ComputeHash(credential), cancellationToken);
            if (session is null)
            {
                return Result(AuthenticationSessionValidationStatus.Unknown);
            }

            if (session.RevokedAt is not null)
            {
                return Result(AuthenticationSessionValidationStatus.Revoked, session);
            }

            if (_timeProvider.GetUtcNow().UtcDateTime >= session.ExpiresAt)
            {
                return Result(AuthenticationSessionValidationStatus.Expired, session);
            }

            var account = await _accountDirectory.FindAsync(session.AccountEID, cancellationToken);
            if (account is { Status: IdentityAccountStatus.Enabled, IsLocalAdministrator: false } && _admissionPolicies.Length == 0)
                return new AuthenticationSessionValidation { Status = AuthenticationSessionValidationStatus.StoreUnavailable,
                    FailureCode = AuthenticationSessionFailureCodes.AdmissionUnavailable };
            if (account is { Status: IdentityAccountStatus.Enabled })
                foreach (var policy in _admissionPolicies)
                    if (!await policy.IsAllowedAsync(account, cancellationToken))
                        return Result(AuthenticationSessionValidationStatus.AccountUnavailable, session);
            // 准入读取可能等待发布；重新确认会话，封闭停用再恢复期间旧会话事实的复活窗口。
            if (_admissionPolicies.Length != 0)
            {
                var current = await _repository.FindByTokenHashAsync(ComputeHash(credential), cancellationToken);
                if (current is null) return Result(AuthenticationSessionValidationStatus.Unknown);
                if (current.RevokedAt is not null) return Result(AuthenticationSessionValidationStatus.Revoked, current);
                if (_timeProvider.GetUtcNow().UtcDateTime >= current.ExpiresAt) return Result(AuthenticationSessionValidationStatus.Expired, current);
            }
            return account is { Status: IdentityAccountStatus.Enabled }
                ? Result(AuthenticationSessionValidationStatus.Active, session)
                : Result(AuthenticationSessionValidationStatus.AccountUnavailable, session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException or PlatformCacheUnavailableException)
        {
            return new AuthenticationSessionValidation
            {
                Status = AuthenticationSessionValidationStatus.StoreUnavailable,
                FailureCode = exception is PlatformCacheUnavailableException ? "Persistence.CacheUnavailable" : AuthenticationSessionFailureCodes.StoreUnavailable
            };
        }
    }

    public ValueTask<bool> RevokeCurrentAsync(
        AuthenticationSessionRevocation revocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.SessionEid);
        var reason = NormalizeReason(revocation.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.ActorId);
        return _repository.RevokeCurrentAsync(
            revocation.SessionEid,
            _timeProvider.GetUtcNow().UtcDateTime,
            reason,
            revocation.ActorId,
            cancellationToken);
    }

    public ValueTask<int> RevokeAllAsync(
        AccountAuthenticationSessionsRevocation revocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.AccountEid);
        var reason = NormalizeReason(revocation.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.ActorId);
        return _repository.RevokeAllAsync(
            revocation.AccountEid,
            _timeProvider.GetUtcNow().UtcDateTime,
            reason,
            revocation.ActorId,
            cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<int> RevokeAccountsAsync(IReadOnlyCollection<string> accountIds, string reason, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (accountIds.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Account identifiers must not be empty.", nameof(accountIds));
        return _repository.RevokeAccountsAsync(accountIds, _timeProvider.GetUtcNow().UtcDateTime, NormalizeReason(reason), actorId, cancellationToken);
    }

    private static string ComputeHash(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential))).ToLowerInvariant();

    private static void EnsureCredentialIs256Bit(string credential)
    {
        try
        {
            var padded = credential.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            if (Convert.FromBase64String(padded).Length == 32)
            {
                return;
            }
        }
        catch (FormatException)
        {
        }

        throw new InvalidOperationException("Authentication session credential generator must issue exactly 256 bits.");
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 500)
        {
            throw new ArgumentException("Authentication session revocation reason must contain 1-500 characters.", nameof(reason));
        }

        return normalized;
    }

    private static AuthenticationSessionValidation Result(
        AuthenticationSessionValidationStatus status,
        AuthenticationSession? session = null) => new()
        {
            Status = status,
            Session = session is null ? null : ToSnapshot(session)
        };

    // 只把已持久化的可信会话事实投影给 Host，不从请求重新推断时区。
    private static AuthenticationSessionSnapshot ToSnapshot(AuthenticationSession session) => new()
    {
        SessionEid = session.EID,
        AccountEid = session.AccountEID,
        Type = session.SessionType,
        TimeZoneId = session.TimeZoneId,
        ExpiresAtUtc = session.ExpiresAt,
        RevokedAtUtc = session.RevokedAt,
        RevocationReason = session.RevocationReason
    };
}
