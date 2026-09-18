using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>拥有本地凭据登录、功能位置读取与认证会话签发的完整应用用例。</summary>
public sealed class IdentityLoginApplicationService : IIdentityLoginApplicationService
{
    private readonly ILocalCredentialAuthenticator _credentialAuthenticator;
    private readonly IFeatureGrantProjection? _featureGrants;
    private readonly IAuthenticationSessionRegistry _sessionRegistry;
    private readonly IdentityAdmissionTransactionCoordinator _admissionCoordinator;

    // 依赖均为既有窄 seam；Identity 只编排，不接管 Authorization 或 Authentication 数据。
    internal IdentityLoginApplicationService(
        ILocalCredentialAuthenticator credentialAuthenticator,
        IFeatureGrantProjection? featureGrants,
        IAuthenticationSessionRegistry sessionRegistry,
        IdentityAdmissionTransactionCoordinator admissionCoordinator)
    {
        _credentialAuthenticator = credentialAuthenticator;
        _featureGrants = featureGrants;
        _sessionRegistry = sessionRegistry;
        _admissionCoordinator = admissionCoordinator;
    }

    /// <inheritdoc/>
    public async ValueTask<IdentityLoginResult> LoginAsync(
        IdentityLoginCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authentication = await _credentialAuthenticator.AuthenticateAsync(
            command.ECODE,
            command.Password,
            cancellationToken);
        if (authentication.Status == LocalCredentialAuthenticationStatus.StoreUnavailable)
        {
            return new IdentityLoginResult
            {
                Status = IdentityLoginStatus.CredentialStoreUnavailable,
                FailureCode = authentication.FailureCode
                    ?? LocalCredentialAuthenticationFailureCodes.StoreUnavailable
            };
        }

        if (authentication is not { Status: LocalCredentialAuthenticationStatus.Succeeded, Account: not null })
        {
            return new IdentityLoginResult
            {
                Status = IdentityLoginStatus.InvalidCredentials,
                FailureCode = IdentityLoginFailureCodes.InvalidCredentials
            };
        }

        IReadOnlyList<string> features;
        try
        {
            features = _featureGrants is null
                ? []
                : await _featureGrants.GetCurrentPositionsAsync(authentication.Account.AccountEid, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new IdentityLoginResult
            {
                Status = IdentityLoginStatus.AuthorizationUnavailable,
                FailureCode = IdentityLoginFailureCodes.AuthorizationUnavailable
            };
        }

        var issuance = await _admissionCoordinator.ExecuteAsync(
            authentication.Account,
            async token =>
            {
                // 等待组织协调权后必须重新验证同一凭据和账号版本，旧结果不能继续签发。
                var current = await _credentialAuthenticator.AuthenticateAsync(
                    command.ECODE,
                    command.Password,
                    token);
                if (current.Status == LocalCredentialAuthenticationStatus.StoreUnavailable)
                {
                    throw new AuthenticationSessionException(AuthenticationSessionFailureCodes.StoreUnavailable);
                }

                if (current is not { Status: LocalCredentialAuthenticationStatus.Succeeded, Account: not null }
                    || current.Account.Revision != authentication.Account.Revision)
                {
                    throw new AuthenticationSessionException(AuthenticationSessionFailureCodes.AdmissionDenied);
                }

                return await _sessionRegistry.RegisterAsync(new AuthenticationSessionRegistration
                {
                    AccountEid = authentication.Account.AccountEid,
                    Type = authentication.IsTemporary
                        ? AuthenticationSessionType.Restricted
                        : AuthenticationSessionType.Regular,
                    ActorId = authentication.Account.AccountEid,
                    TimeZoneId = command.TimeZoneId
                }, token);
            },
            cancellationToken);

        return new IdentityLoginResult
        {
            Status = IdentityLoginStatus.Succeeded,
            Account = authentication.Account,
            IsTemporary = authentication.IsTemporary,
            Issuance = issuance,
            FeaturePositions = features
        };
    }
}
