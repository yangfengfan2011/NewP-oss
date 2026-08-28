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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Authentication.Tests;

public sealed class AuthenticationSessionSqlServerIntegrationTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_AUTHENTICATION_SQLSERVER_INTEGRATION";
    private const string AdminConnectionEnvironmentVariable = "NEWP_AUTHENTICATION_SQLSERVER_ADMIN_CONNECTION";
    private static readonly DateTime InitialTime = new(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_EnforcesDigestUniquenessAndMakesConcurrentRevocationRollbackAndPurgeAtomic()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPAuthentication34_{Guid.NewGuid():N}";
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        var applicationConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog
        }.ConnectionString;

        await CreateCatalogAsync(adminConnectionString, catalog);
        try
        {
            await using (var setup = CreateContext(applicationConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Set<Dictionary<string, object?>>(setup.ResolveModelKey("Identity/IdentityAccount")).Add(Account("User_1"));
                await setup.SaveChangesAsync();
            }

            const string repeatedCredential = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
            await using (var firstContext = CreateContext(applicationConnectionString))
            {
                var registry = CreateRegistry(firstContext, new FixedCredentialGenerator(repeatedCredential));
                await new EfPlatformTransactionExecutor(firstContext).ExecuteAsync(token => IssueAsync(registry, token));
            }

            await using (var collisionContext = CreateContext(applicationConnectionString))
            {
                var registry = CreateRegistry(collisionContext, new FixedCredentialGenerator(repeatedCredential));
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    new EfPlatformTransactionExecutor(collisionContext).ExecuteAsync(token => IssueAsync(registry, token)).AsTask());
            }

            AuthenticationSessionIssuance rollbackSession;
            await using (var issuanceContext = CreateContext(applicationConnectionString))
            {
                var registry = CreateRegistry(issuanceContext);
                rollbackSession = await new EfPlatformTransactionExecutor(issuanceContext)
                    .ExecuteAsync(token => IssueAsync(registry, token, "America/Toronto"));
            }

            await using (var rollbackContext = CreateContext(applicationConnectionString))
            {
                var registry = CreateRegistry(rollbackContext);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new EfPlatformTransactionExecutor(rollbackContext).ExecuteAsync<string>(async token =>
                    {
                        Assert.Equal(2, await registry.RevokeAllAsync(new AccountAuthenticationSessionsRevocation
                        {
                            AccountEid = "User_1",
                            Reason = "injected rollback",
                            ActorId = "User_1"
                        }, token));
                        throw new InvalidOperationException("injected after revocation");
                    }).AsTask());
            }

            await using (var verification = CreateContext(applicationConnectionString))
            {
                var restored = await CreateRegistry(verification).ValidateAsync(rollbackSession.Credential);
                Assert.Equal(AuthenticationSessionValidationStatus.Active, restored.Status);
                Assert.Equal("America/Toronto", restored.Session?.TimeZoneId);
            }

            var sessionCountBeforeRegistrationRollback = await CountSessionsAsync(applicationConnectionString);
            await using (var registrationRollbackContext = CreateContext(applicationConnectionString))
            {
                var registry = CreateRegistry(registrationRollbackContext);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new EfPlatformTransactionExecutor(registrationRollbackContext).ExecuteAsync<string>(async token =>
                    {
                        await IssueAsync(registry, token);
                        throw new InvalidOperationException("injected after registration");
                    }).AsTask());
            }
            Assert.Equal(sessionCountBeforeRegistrationRollback, await CountSessionsAsync(applicationConnectionString));

            AuthenticationSessionIssuance concurrentSession;
            await using (var issuanceContext = CreateContext(applicationConnectionString))
            {
                concurrentSession = await new EfPlatformTransactionExecutor(issuanceContext)
                    .ExecuteAsync(token => IssueAsync(CreateRegistry(issuanceContext), token));
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrivals = 0;
            async Task<bool> RevokeConcurrentlyAsync(string reason)
            {
                await using var context = CreateContext(applicationConnectionString);
                var registry = CreateRegistry(context);
                return await new EfPlatformTransactionExecutor(context).ExecuteAsync(async token =>
                {
                    if (Interlocked.Increment(ref arrivals) == 2)
                    {
                        gate.SetResult();
                    }

                    await gate.Task.WaitAsync(token);
                    return await registry.RevokeCurrentAsync(new AuthenticationSessionRevocation
                    {
                        SessionEid = concurrentSession.Session.SessionEid,
                        Reason = reason,
                        ActorId = "User_1"
                    }, token);
                });
            }

            var concurrentResults = await Task.WhenAll(
                RevokeConcurrentlyAsync("first request"),
                RevokeConcurrentlyAsync("second request"));
            Assert.Single(concurrentResults, static result => result);

            var boundaryTime = new MutableTimeProvider(InitialTime.AddHours(8).AddDays(30));
            await using (var purgeContext = CreateContext(applicationConnectionString))
            {
                var repository = new EfAuthenticationSessionRepository(purgeContext);
                var purged = await new AuthenticationSessionPurgeService(
                    repository,
                    new EfPlatformTransactionExecutor(purgeContext),
                    boundaryTime,
                    new AuthenticationSessionOptions { PurgeBatchSize = 1 })
                    .PurgeAsync();
                Assert.Equal(1, purged);
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
        }
    }

    private static AuthenticationSessionRegistry CreateRegistry(
        EfPlatformDbContext context,
        IAuthenticationSessionCredentialGenerator? credentialGenerator = null) => new(
            new EfAuthenticationSessionRepository(context),
            new EnabledAccountDirectory(),
            new GuidObjectIdGenerator(),
            credentialGenerator ?? new DefaultAuthenticationSessionCredentialGenerator(),
            new MutableTimeProvider(InitialTime),
            new AuthenticationSessionOptions());

    private static ValueTask<AuthenticationSessionIssuance> IssueAsync(
        IAuthenticationSessionRegistry registry,
        CancellationToken cancellationToken,
        string? timeZoneId = null) => registry.RegisterAsync(new AuthenticationSessionRegistration
        {
            AccountEid = "User_1",
            Type = AuthenticationSessionType.Regular,
            ActorId = "User_1",
            TimeZoneId = timeZoneId
        }, cancellationToken);

    private static EfPlatformDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(
            options,
            IdentityModelDefinitions.All.Concat(AuthenticationModelDefinitions.All).ToArray());
    }

    private static Dictionary<string, object?> Account(string accountEid) => new(StringComparer.Ordinal)
    {
        ["EID"] = accountEid,
        ["ECODE"] = "alice",
        ["NormalizedECODE"] = "ALICE",
        ["ENAME"] = "Alice",
        ["Status"] = IdentityAccountStatus.Enabled.ToString(),
        ["IsLocalAdministrator"] = false,
        ["IsPersonnelBound"] = false,
        ["PersonnelDisableHold"] = false,
        ["Revision"] = 1L,
        ["CreatedBy"] = "system",
        ["CreatedAt"] = InitialTime,
        ["UpdatedBy"] = "system",
        ["UpdatedAt"] = InitialTime
    };

    private static async Task<int> CountSessionsAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        return await context
            .Set<Dictionary<string, object?>>(context.ResolveModelKey("Authentication/AuthenticationSession"))
            .CountAsync();
    }

    private static async Task CreateCatalogAsync(string connectionString, string catalog)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropCatalogAsync(string connectionString, string catalog)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{catalog}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{catalog}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedCredentialGenerator(string credential) : IAuthenticationSessionCredentialGenerator
    {
        public string Issue() => credential;
    }

    private sealed class GuidObjectIdGenerator : IPlatformObjectIdGenerator
    {
        public string NewId(string objectName) => $"{objectName}_{Guid.NewGuid():N}";
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class EnabledAccountDirectory : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityAccountSnapshot?>(new IdentityAccountSnapshot
            {
                AccountEid = accountEid,
                ECODE = "alice",
                ENAME = "Alice",
                Status = IdentityAccountStatus.Enabled,
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
                            Status = IdentityAccountStatus.Enabled,
                            Revision = 1
                        },
                        StringComparer.Ordinal));

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
