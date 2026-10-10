using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Persistence.Abstractions;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityLoginApplicationServiceTests
{
    [Fact]
    public async Task LoginAsync_ValidTemporaryCredentialReturnsRestrictedSessionAndFeaturePositions()
    {
        var account = Account(revision: 7);
        var sessions = new RecordingSessionRegistry();
        var transaction = new InlineTransactionExecutor();
        IIdentityLoginApplicationService service = new IdentityLoginApplicationService(
            new SequenceAuthenticator(Succeeded(account, isTemporary: true), Succeeded(account, isTemporary: true)),
            new FixedFeatureProjection(["organization.person.read"]),
            sessions,
            new IdentityAdmissionTransactionCoordinator(transaction, [new FixedAdmissionPolicy()]));

        var result = await service.LoginAsync(
            new IdentityLoginCommand("alice", "secret", "Europe/Paris"),
            CancellationToken.None);

        Assert.Equal(IdentityLoginStatus.Succeeded, result.Status);
        Assert.Equal(account, result.Account);
        Assert.True(result.IsTemporary);
        Assert.Equal(["organization.person.read"], result.FeaturePositions);
        Assert.Equal(AuthenticationSessionType.Restricted, result.Issuance?.Session.Type);
        Assert.Equal("Europe/Paris", result.Issuance?.Session.TimeZoneId);
        Assert.Equal("issued-credential", result.Issuance?.Credential);
    }

    private static IdentityAccountSnapshot Account(long revision) => new()
    {
        AccountEid = "IdentityAccount_1",
        ECODE = "alice",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        Revision = revision
    };

    private static LocalCredentialAuthenticationResult Succeeded(
        IdentityAccountSnapshot account,
        bool isTemporary) => new()
    {
        Status = LocalCredentialAuthenticationStatus.Succeeded,
        Account = account,
        IsTemporary = isTemporary
    };

    private sealed class SequenceAuthenticator(params LocalCredentialAuthenticationResult[] results)
        : ILocalCredentialAuthenticator
    {
        private readonly Queue<LocalCredentialAuthenticationResult> _results = new(results);

        public ValueTask<LocalCredentialAuthenticationResult> AuthenticateAsync(
            string accountCode,
            string password,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(_results.Dequeue());
    }

    private sealed class FixedFeatureProjection(IReadOnlyList<string> positions) : IFeatureGrantProjection
    {
        public Task<IReadOnlyList<string>> GetCurrentPositionsAsync(
            string subjectId,
            CancellationToken cancellationToken) => Task.FromResult(positions);
    }

    private sealed class InlineTransactionExecutor : IPlatformTransactionExecutor
    {
        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) => registerChanges(cancellationToken);
    }

    private sealed class FixedAdmissionPolicy : IIdentityAdmissionPolicy
    {
        public IReadOnlyCollection<string> GetCoordinationModels(IdentityAccountSnapshot account) => ["Person"];

        public ValueTask<bool> IsAllowedAsync(
            IdentityAccountSnapshot account,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class RecordingSessionRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(
            AuthenticationSessionRegistration registration,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuthenticationSessionIssuance
        {
            Credential = "issued-credential",
            Session = new AuthenticationSessionSnapshot
            {
                SessionEid = "AuthenticationSession_1",
                AccountEid = registration.AccountEid,
                Type = registration.Type,
                TimeZoneId = registration.TimeZoneId ?? "Asia/Shanghai",
                ExpiresAtUtc = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc)
            }
        });

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(
            string credential,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> RevokeCurrentAsync(
            AuthenticationSessionRevocation revocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> RevokeAllAsync(
            AccountAuthenticationSessionsRevocation revocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
