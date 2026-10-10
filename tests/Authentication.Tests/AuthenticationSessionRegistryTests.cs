using System.Data.Common;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication.Persistence.Ef;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.EfCore.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ICP.Platform.Authentication.Tests;

public sealed class AuthenticationSessionRegistryTests
{
    [Fact]
    public async Task MissingAdmissionOwner_DoesNotInvalidateExistingSessionOrIssueAnOrdinarySession()
    {
        await using var context = CreateContext();
        var admitted = CreateRegistry(context);
        var issuance = await new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(admitted, cancellationToken: token));
        var missing = CreateRegistry(context, policies: []);
        var unavailable = await missing.ValidateAsync(issuance.Credential);
        Assert.Equal(AuthenticationSessionValidationStatus.StoreUnavailable, unavailable.Status);
        Assert.Equal(AuthenticationSessionFailureCodes.AdmissionUnavailable, unavailable.FailureCode);
        var failure = await Assert.ThrowsAsync<AuthenticationSessionException>(() =>
            new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(missing, cancellationToken: token)).AsTask());
        Assert.Equal(AuthenticationSessionFailureCodes.AdmissionUnavailable, failure.FailureCode);
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await admitted.ValidateAsync(issuance.Credential)).Status);
        Assert.Single(await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("AuthenticationSession")).ToListAsync());
    }
    // 本fixture仅证明会话或跨owner事务；完整组织准入由Host真实业务fixture另行证明。
    internal sealed class IsolatedAdmissionPolicy : IIdentityAdmissionPolicy
    {
        public IReadOnlyCollection<string> GetCoordinationModels(IdentityAccountSnapshot account) => [];
        public ValueTask<bool> IsAllowedAsync(IdentityAccountSnapshot account, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    [Fact]
    public async Task RegisterAsync_IssuesOneTime256BitCredentialAndPersistsOnlyItsDigest()
    {
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var repository = new EfAuthenticationSessionRepository(context);
        var registry = new AuthenticationSessionRegistry(
            repository,
            new FixedAccountDirectory(IdentityAccountStatus.Enabled),
            new GuidObjectIdGenerator(),
            new DefaultAuthenticationSessionCredentialGenerator(),
            new FixedTimeProvider(now),
            new AuthenticationSessionOptions(), [new IsolatedAdmissionPolicy()]);

        var issuance = await new EfPlatformTransactionExecutor(context).ExecuteAsync(
            token => registry.RegisterAsync(new AuthenticationSessionRegistration
            {
                AccountEid = "User_1",
                Type = AuthenticationSessionType.Regular,
                ActorId = "User_1"
            }, token));

        Assert.Equal(32, DecodeCredential(issuance.Credential).Length);
        Assert.Equal(now.AddHours(8), issuance.Session.ExpiresAtUtc);
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(issuance.Credential)).Status);
        Assert.Equal(AuthenticationSessionValidationStatus.Unknown, (await registry.ValidateAsync(issuance.Session.SessionEid)).Status);
        var stored = Assert.Single(await context
            .Set<Dictionary<string, object?>>(context.ResolveModelKey("AuthenticationSession"))
            .AsNoTracking()
            .ToListAsync());
        Assert.NotEqual(issuance.Credential, stored["TokenHash"]);
        Assert.DoesNotContain(issuance.Credential, string.Join('|', stored.Values));
    }

    [Fact]
    public async Task RegisterAsync_FreezesExplicitOrConfiguredIanaTimeZoneAndRejectsInvalidInput()
    {
        await using var context = CreateContext();
        var registry = CreateRegistry(context, options: new AuthenticationSessionOptions
        {
            DefaultTimeZoneId = "Asia/Taipei"
        });
        var transaction = new EfPlatformTransactionExecutor(context);

        var configured = await transaction.ExecuteAsync(token => IssueAsync(registry, cancellationToken: token));
        var explicitZone = await transaction.ExecuteAsync(token => IssueAsync(
            registry,
            timeZoneId: "Europe/Paris",
            cancellationToken: token));

        Assert.Equal("Asia/Taipei", configured.Session.TimeZoneId);
        Assert.Equal("Europe/Paris", explicitZone.Session.TimeZoneId);
        Assert.Equal(
            "Asia/Taipei",
            (await registry.ValidateAsync(configured.Credential)).Session!.TimeZoneId);
        var exception = await Assert.ThrowsAsync<AuthenticationSessionException>(async () =>
            await transaction.ExecuteAsync(token => IssueAsync(
                registry,
                timeZoneId: "China Standard Time",
                cancellationToken: token)));
        Assert.Equal(AuthenticationSessionFailureCodes.InvalidTimeZone, exception.FailureCode);
        Assert.Equal(2, await context
            .Set<Dictionary<string, object?>>(context.ResolveModelKey("AuthenticationSession"))
            .CountAsync());
    }

    [Theory]
    [InlineData("America/Toronto")]
    [InlineData("America/Atikokan")]
    public async Task RegisterAsync_PreservesTheValidatedIanaZoneId(string timeZoneId)
    {
        await using var context = CreateContext();
        var registry = CreateRegistry(context);

        var issuance = await new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(
            registry,
            timeZoneId: timeZoneId,
            cancellationToken: token));

        Assert.Equal(timeZoneId, issuance.Session.TimeZoneId);
        Assert.Equal(timeZoneId, (await registry.ValidateAsync(issuance.Credential)).Session!.TimeZoneId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    public async Task RegisterAsync_RejectsExplicitInvalidTimeZoneBeforeAddingSession(string timeZoneId)
    {
        await using var context = CreateContext();
        var registry = CreateRegistry(context);

        var exception = await Assert.ThrowsAsync<AuthenticationSessionException>(async () =>
            await new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(
                registry,
                timeZoneId: timeZoneId,
                cancellationToken: token)));

        Assert.Equal(AuthenticationSessionFailureCodes.InvalidTimeZone, exception.FailureCode);
        Assert.Empty(await context
            .Set<Dictionary<string, object?>>(context.ResolveModelKey("AuthenticationSession"))
            .ToListAsync());
    }

    [Fact]
    public async Task Registry_AllowsMultipleSessionsAndReturnsRestrictedType()
    {
        await using var context = CreateContext();
        var registry = CreateRegistry(context);
        var transaction = new EfPlatformTransactionExecutor(context);

        var regular = await transaction.ExecuteAsync(token => IssueAsync(registry, AuthenticationSessionType.Regular, cancellationToken: token));
        var restricted = await transaction.ExecuteAsync(token => IssueAsync(registry, AuthenticationSessionType.Restricted, cancellationToken: token));

        Assert.NotEqual(regular.Credential, restricted.Credential);
        Assert.Equal(AuthenticationSessionType.Restricted, (await registry.ValidateAsync(restricted.Credential)).Session!.Type);
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(regular.Credential)).Status);
        Assert.Equal(2, await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("AuthenticationSession")).CountAsync());
    }

    [Fact]
    public async Task ValidateAsync_UsesFixedExpiryBoundaryWithoutSlidingRenewal()
    {
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var time = new MutableTimeProvider(now);
        await using var context = CreateContext();
        var registry = CreateRegistry(context, timeProvider: time, options: new AuthenticationSessionOptions
        {
            Lifetime = TimeSpan.FromMinutes(5)
        });
        var issuance = await new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(registry, cancellationToken: token));

        Assert.False(await registry.IsActiveAsync(issuance.Session.SessionEid, "other-account"));
        Assert.False(await registry.IsActiveAsync("unknown", "User_1"));
        Assert.False(await registry.IsActiveAsync("", "User_1"));
        time.UtcNow = now.AddMinutes(4).AddSeconds(59);
        Assert.True(await registry.IsActiveAsync(issuance.Session.SessionEid, "User_1"));
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(issuance.Credential)).Status);
        Assert.Equal(now.AddMinutes(5), issuance.Session.ExpiresAtUtc);
        time.UtcNow = now.AddMinutes(5);
        Assert.False(await registry.IsActiveAsync(issuance.Session.SessionEid, "User_1"));
        Assert.Equal(AuthenticationSessionValidationStatus.Expired, (await registry.ValidateAsync(issuance.Credential)).Status);
        Assert.Equal(now.AddMinutes(5), issuance.Session.ExpiresAtUtc);
    }

    [Fact]
    public async Task ValidateAsync_DistinguishesInvalidAccountAndStoreFailure()
    {
        await using var context = CreateContext();
        var disabledRegistry = CreateRegistry(
            context,
            accountDirectory: new FixedAccountDirectory(IdentityAccountStatus.Disabled));
        var issuance = await new EfPlatformTransactionExecutor(context).ExecuteAsync(token => IssueAsync(CreateRegistry(context), cancellationToken: token));

        Assert.Equal(AuthenticationSessionValidationStatus.Unknown, (await disabledRegistry.ValidateAsync("unknown")).Status);
        Assert.Equal(AuthenticationSessionValidationStatus.AccountUnavailable, (await disabledRegistry.ValidateAsync(issuance.Credential)).Status);

        var failed = await CreateRegistry(context, accountDirectory: new ThrowingAccountDirectory()).ValidateAsync(issuance.Credential);
        Assert.Equal(AuthenticationSessionValidationStatus.StoreUnavailable, failed.Status);
        Assert.Equal(AuthenticationSessionFailureCodes.StoreUnavailable, failed.FailureCode);
    }

    [Fact]
    public async Task RevokeCurrentAndAll_OnlyInvalidateTheirIntendedSessions()
    {
        await using var context = CreateContext();
        var registry = CreateRegistry(context);
        var transaction = new EfPlatformTransactionExecutor(context);
        var first = await transaction.ExecuteAsync(token => IssueAsync(registry, cancellationToken: token));
        var second = await transaction.ExecuteAsync(token => IssueAsync(registry, cancellationToken: token));
        var otherUser = await transaction.ExecuteAsync(token => IssueAsync(registry, accountEid: "User_2", cancellationToken: token));

        Assert.True(await transaction.ExecuteAsync(token => registry.RevokeCurrentAsync(new AuthenticationSessionRevocation
        {
            SessionEid = first.Session.SessionEid,
            Reason = "logout",
            ActorId = "User_1"
        }, token)));
        Assert.False(await registry.IsActiveAsync(first.Session.SessionEid, "User_1"));
        var revoked = await registry.ValidateAsync(first.Credential);
        Assert.Equal(AuthenticationSessionValidationStatus.Revoked, revoked.Status);
        Assert.Equal("logout", revoked.Session!.RevocationReason);
        Assert.Equal(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), revoked.Session.RevokedAtUtc);
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(second.Credential)).Status);

        Assert.Equal(1, await transaction.ExecuteAsync(token => registry.RevokeAllAsync(new AccountAuthenticationSessionsRevocation
        {
            AccountEid = "User_1",
            Reason = "credential changed",
            ActorId = "User_1"
        }, token)));
        Assert.Equal(AuthenticationSessionValidationStatus.Revoked, (await registry.ValidateAsync(second.Credential)).Status);
        Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(otherUser.Credential)).Status);
    }

    [Fact]
    public async Task PurgeAsync_DeletesOnlyThirtyDayOldTerminalSessionsInBoundedBatches()
    {
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var time = new MutableTimeProvider(now);
        await using var context = CreateContext();
        var options = new AuthenticationSessionOptions { Lifetime = TimeSpan.FromHours(1), PurgeBatchSize = 2 };
        var repository = new EfAuthenticationSessionRepository(context);
        var registry = CreateRegistry(context, timeProvider: time, options: options);
        var transaction = new EfPlatformTransactionExecutor(context);
        var sessions = new List<AuthenticationSessionIssuance>();
        for (var index = 0; index < 3; index++)
        {
            sessions.Add(await transaction.ExecuteAsync(token => IssueAsync(registry, cancellationToken: token)));
        }
        time.UtcNow = now.AddHours(1);
        var recent = await transaction.ExecuteAsync(token => IssueAsync(registry, cancellationToken: token));

        time.UtcNow = now.AddHours(1).AddDays(30);
        var purge = new AuthenticationSessionPurgeService(repository, transaction, time, options);

        Assert.Equal(2, await purge.PurgeAsync());
        Assert.Equal(1, await purge.PurgeAsync());
        Assert.Equal(0, await purge.PurgeAsync());
        Assert.Equal(AuthenticationSessionValidationStatus.Unknown, (await registry.ValidateAsync(sessions[0].Credential)).Status);
        Assert.Equal(AuthenticationSessionValidationStatus.Unknown, (await registry.ValidateAsync(sessions[2].Credential)).Status);
        Assert.Equal(AuthenticationSessionValidationStatus.Expired, (await registry.ValidateAsync(recent.Credential)).Status);
    }

    private static byte[] DecodeCredential(string credential)
    {
        var padded = credential.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Convert.FromBase64String(padded);
    }

    private static AuthenticationSessionRegistry CreateRegistry(
        EfPlatformDbContext context,
        IIdentityAccountDirectory? accountDirectory = null,
        TimeProvider? timeProvider = null,
        AuthenticationSessionOptions? options = null,
        IIdentityAdmissionPolicy[]? policies = null) => new(
            new EfAuthenticationSessionRepository(context),
            accountDirectory ?? new FixedAccountDirectory(IdentityAccountStatus.Enabled),
            new GuidObjectIdGenerator(),
            new DefaultAuthenticationSessionCredentialGenerator(),
            timeProvider ?? new FixedTimeProvider(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc)),
            options ?? new AuthenticationSessionOptions(), policies ?? [new IsolatedAdmissionPolicy()]);

    private static ValueTask<AuthenticationSessionIssuance> IssueAsync(
        IAuthenticationSessionRegistry registry,
        AuthenticationSessionType type = AuthenticationSessionType.Regular,
        string accountEid = "User_1",
        string? timeZoneId = null,
        CancellationToken cancellationToken = default) => registry.RegisterAsync(new AuthenticationSessionRegistration
        {
            AccountEid = accountEid,
            Type = type,
            ActorId = "User_1",
            TimeZoneId = timeZoneId
        }, cancellationToken);

    private static EfPlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(
            options,
            IdentityModelDefinitions.All.Concat(AuthenticationModelDefinitions.All).ToArray());
    }

    private sealed class FixedAccountDirectory(IdentityAccountStatus status) : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityAccountSnapshot?>(new IdentityAccountSnapshot
            {
                AccountEid = accountEid,
                ECODE = "alice",
                ENAME = "Alice",
                Status = status,
                Revision = 1
            });

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(
                accountEids
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(
                        accountEid => accountEid,
                        accountEid => new IdentityAccountSnapshot
                        {
                            AccountEid = accountEid,
                            ECODE = "alice",
                            ENAME = "Alice",
                            Status = status,
                            Revision = 1
                        },
                        StringComparer.Ordinal));

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingAccountDirectory : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            throw new SimulatedStoreException();

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            throw new SimulatedStoreException();

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SimulatedStoreException() : DbException("database unavailable");

    private sealed class GuidObjectIdGenerator : IPlatformObjectIdGenerator
    {
        public string NewId(string objectName) => $"{objectName}_{Guid.NewGuid():N}";
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }
}
