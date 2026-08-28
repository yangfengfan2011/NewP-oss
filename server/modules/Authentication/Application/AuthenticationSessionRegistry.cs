using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Models;
using ICP.Platform.Modules.Authentication.Repositories;

namespace ICP.Platform.Modules.Authentication.Application;

public sealed class AuthenticationSessionRegistry : IAuthenticationSessionRegistry
{
    private readonly IAuthenticationSessionRepository _repository;
    private readonly IIdentityAccountDirectory _accountDirectory;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly IAuthenticationSessionCredentialGenerator _credentialGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationSessionOptions _options;

    public AuthenticationSessionRegistry(
        IAuthenticationSessionRepository repository,
        IIdentityAccountDirectory accountDirectory,
        IPlatformObjectIdGenerator objectIdGenerator,
        IAuthenticationSessionCredentialGenerator credentialGenerator,
        TimeProvider timeProvider,
        AuthenticationSessionOptions options)
    {
        _repository = repository;
        _accountDirectory = accountDirectory;
        _objectIdGenerator = objectIdGenerator;
        _credentialGenerator = credentialGenerator;
        _timeProvider = timeProvider;
        _options = options;
    }

    // 在生成凭据和登记实体之前解析最终会话时区，确保非法输入没有持久化副作用。
    public ValueTask<AuthenticationSessionIssuance> RegisterAsync(
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
        return ValueTask.FromResult(new AuthenticationSessionIssuance
        {
            Credential = credential,
            Session = ToSnapshot(session)
        });
    }

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
            return account is { Status: IdentityAccountStatus.Enabled }
                ? Result(AuthenticationSessionValidationStatus.Active, session)
                : Result(AuthenticationSessionValidationStatus.AccountUnavailable, session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            return new AuthenticationSessionValidation
            {
                Status = AuthenticationSessionValidationStatus.StoreUnavailable,
                FailureCode = AuthenticationSessionFailureCodes.StoreUnavailable
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
