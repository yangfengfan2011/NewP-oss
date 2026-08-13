using ICP.Platform.Identity.Contracts;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication.Persistence.Ef;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.EfCore.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentitySqlServerIntegrationTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_IDENTITY_SQLSERVER_INTEGRATION";
    private const string AdminConnectionEnvironmentVariable = "NEWP_IDENTITY_SQLSERVER_ADMIN_CONNECTION";

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_WhenLocalAdminRoleAssignmentFails_RollsBackEveryBootstrapWrite()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPIdentity33Rollback_{Guid.NewGuid():N}";
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        var applicationConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog
        }.ConnectionString;

        await CreateCatalogAsync(adminConnectionString, catalog);
        try
        {
            await using (var setup = CreateIdentityAndAuthorizationContext(applicationConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            await using (var context = CreateIdentityAndAuthorizationContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(context);
                var ids = new GuidObjectIdGenerator();
                var service = new IdentityLocalAdministratorMaintenanceService(
                    repository,
                    new EfPlatformTransactionExecutor(context),
                    new NoOpSessionRegistry(),
                    new EfLocalAdministratorRoleAssignment(context, ids),
                    new PasswordHasher<IdentityAccount>(),
                    new FixedCredentialIssuer(),
                    ids,
                    TimeProvider.System);

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BootstrapAsync(
                    new BootstrapLocalAdministratorCommand(
                        "admin",
                        "Local Administrator",
                        "rollback test",
                        new IdentityActorContext(
                            IdentityMaintenanceActors.LocalMaintenance,
                            IdentitySecurityActorKind.LocalMaintenance,
                            Guid.NewGuid().ToString("N")))).AsTask());
                Assert.Equal("AUTHORIZATION_LOCAL_ADMIN_ROLE_UNAVAILABLE", exception.Message);
            }

            await using var verification = CreateIdentityAndAuthorizationContext(applicationConnectionString);
            Assert.Empty(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/IdentityAccount")).ToListAsync());
            Assert.Empty(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/LocalCredential")).ToListAsync());
            Assert.Empty(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/IdentitySecurityEvent")).ToListAsync());
            Assert.Empty(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("UserRole")).ToListAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_ConcurrentBootstrapUsesDatabaseMutexAndCreatesExactlyOneAdministrator()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPIdentity33_{Guid.NewGuid():N}";
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        var applicationConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog
        }.ConnectionString;

        await CreateCatalogAsync(adminConnectionString, catalog);
        try
        {
            await using (var setup = CreateIdentityAndAuthorizationContext(applicationConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Set<Dictionary<string, object?>>(setup.ResolveModelKey("Role")).Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["EID"] = "Role_LocalAdmin",
                    ["ECODE"] = "LocalAdmin",
                    ["ENAME"] = "Local Administrator",
                    ["Description"] = null,
                    ["IsEnabled"] = true,
                    ["IsBuiltIn"] = true,
                    ["CreatedAt"] = DateTime.UtcNow,
                    ["CreatedBy"] = "system",
                    ["UpdatedAt"] = null,
                    ["UpdatedBy"] = null
                });
                await setup.SaveChangesAsync();
            }

            var first = BootstrapAsync(applicationConnectionString, "admin-one");
            var second = BootstrapAsync(applicationConnectionString, "admin-two");
            var outcomes = await Task.WhenAll(first, second);

            Assert.Single(outcomes, static result => result == "success");
            Assert.Single(outcomes, static result => result == IdentityMaintenanceFailureCodes.BootstrapAlreadyCompleted);
            await using var verification = CreateIdentityAndAuthorizationContext(applicationConnectionString);
            Assert.Single(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/IdentityAccount")).ToListAsync());
            Assert.Single(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/LocalCredential")).ToListAsync());
            Assert.Single(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("Identity/IdentitySecurityEvent")).ToListAsync());
            Assert.Single(await verification.Set<Dictionary<string, object?>>(verification.ResolveModelKey("UserRole")).ToListAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_RevisionClaimAndFailureInjectionAreAtomic()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPIdentity32_{Guid.NewGuid():N}";
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
                var repository = new EfIdentityRepository(setup);
                var transaction = new EfPlatformTransactionExecutor(setup);
                var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
                await transaction.ExecuteAsync<string>(_ =>
                {
                    repository.AddAccount(AccountAtRevision(1, "Alice", now));
                    repository.AddCredential(new LocalCredential
                    {
                        EID = "LocalCredential_1",
                        AccountEID = "User_1",
                        PasswordHash = "hash-only",
                        IsTemporary = true,
                        CreatedBy = "actor",
                        CreatedAt = now,
                        UpdatedBy = "actor",
                        UpdatedAt = now
                    });
                    repository.AddSecurityEvent(Event("IdentitySecurityEvent_1", "AccountCreated", now));
                    return ValueTask.FromResult("created");
                });
            }

            await using (var failing = CreateContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(failing);
                var transaction = new EfPlatformTransactionExecutor(failing);
                await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync<string>(async token =>
                {
                    var changed = AccountAtRevision(2, "Rolled back", new DateTime(2026, 8, 10, 8, 1, 0, DateTimeKind.Utc));
                    Assert.True(await repository.TryReplaceAccountAsync(changed, 1, token));
                    repository.AddSecurityEvent(Event(
                        "IdentitySecurityEvent_rollback",
                        "DisplayNameChanged",
                        changed.UpdatedAt));
                    throw new InvalidOperationException("injected after revision claim");
                }).AsTask());
            }

            await using (var verification = CreateContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(verification);
                var rolledBack = Assert.IsType<IdentityAccount>(await repository.FindAccountAsync("User_1"));
                Assert.Equal(1, rolledBack.Revision);
                Assert.Equal("Alice", rolledBack.ENAME);
                Assert.Single(await verification
                    .Set<Dictionary<string, object?>>("Identity/IdentitySecurityEvent")
                    .ToListAsync());
            }

            await using (var success = CreateContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(success);
                var transaction = new EfPlatformTransactionExecutor(success);
                await transaction.ExecuteAsync<string>(async token =>
                {
                    var changed = AccountAtRevision(2, "Committed", new DateTime(2026, 8, 10, 8, 2, 0, DateTimeKind.Utc));
                    Assert.True(await repository.TryReplaceAccountAsync(changed, 1, token));
                    repository.AddSecurityEvent(Event(
                        "IdentitySecurityEvent_2",
                        "DisplayNameChanged",
                        changed.UpdatedAt));
                    return "committed";
                });
            }

            await using (var finalVerification = CreateContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(finalVerification);
                var committed = Assert.IsType<IdentityAccount>(await repository.FindAccountAsync("User_1"));
                Assert.Equal(2, committed.Revision);
                Assert.Equal("Committed", committed.ENAME);
                Assert.Equal(2, await finalVerification
                    .Set<Dictionary<string, object?>>("Identity/IdentitySecurityEvent")
                    .CountAsync());
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_PasswordChangeRotatesSessionPagesEventsAndRollsBackFailedResign()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var catalog = $"NewPIdentity37_{Guid.NewGuid():N}";
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        var applicationConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog
        }.ConnectionString;

        await CreateCatalogAsync(adminConnectionString, catalog);
        try
        {
            AuthenticationSessionIssuance oldSession;
            var passwordHasher = new PasswordHasher<IdentityAccount>();
            await using (var setup = CreateIdentityAndAuthenticationContext(applicationConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                var repository = new EfIdentityRepository(setup);
                var transaction = new EfPlatformTransactionExecutor(setup);
                var account = AccountAtRevision(1, "Alice", new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc));
                oldSession = await transaction.ExecuteAsync(async token =>
                {
                    repository.AddAccount(account);
                    repository.AddCredential(new LocalCredential
                    {
                        EID = "LocalCredential_1",
                        AccountEID = account.EID,
                        PasswordHash = passwordHasher.HashPassword(account, "initial-secret"),
                        IsTemporary = true,
                        CreatedBy = account.EID,
                        CreatedAt = account.CreatedAt,
                        UpdatedBy = account.EID,
                        UpdatedAt = account.CreatedAt
                    });
                    repository.AddSecurityEvent(Event("IdentitySecurityEvent_1", "AccountCreated", account.CreatedAt));
                    return await CreateSessionRegistry(setup, repository).RegisterAsync(
                        new AuthenticationSessionRegistration
                        {
                            AccountEid = account.EID,
                            Type = AuthenticationSessionType.Restricted,
                            ActorId = account.EID
                        },
                        token);
                });
            }

            IdentityPasswordChangeResult changed;
            await using (var change = CreateIdentityAndAuthenticationContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(change);
                var registry = CreateSessionRegistry(change, repository);
                changed = await CreateAccountService(change, repository, registry, passwordHasher)
                    .ChangePasswordAsync(new ChangeIdentityPasswordCommand(
                        "User_1",
                        "initial-secret",
                        "replacement-secret",
                        1,
                        new IdentityActorContext("User_1", IdentitySecurityActorKind.IdentityAccount, "trace-change")));
            }

            await using (var verification = CreateIdentityAndAuthenticationContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(verification);
                var registry = CreateSessionRegistry(verification, repository);
                Assert.Equal(AuthenticationSessionValidationStatus.Revoked, (await registry.ValidateAsync(oldSession.Credential)).Status);
                Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(changed.Session.Credential)).Status);
                var account = Assert.IsType<IdentityAccount>(await repository.FindAccountAsync("User_1"));
                var credential = Assert.IsType<LocalCredential>(await repository.FindCredentialAsync("User_1"));
                Assert.Equal(2, account.Revision);
                Assert.False(credential.IsTemporary);
                Assert.NotEqual(PasswordVerificationResult.Failed, passwordHasher.VerifyHashedPassword(account, credential.PasswordHash, "replacement-secret"));
                var detail = await new IdentityManagementQueryService(repository).FindAsync("User_1", 1, 1);
                Assert.Equal(2, detail?.Events.TotalCount);
                Assert.Equal(IdentitySecurityEventTypes.PasswordChanged, Assert.Single(detail!.Events.Items).EventType);
            }

            await using (var failing = CreateIdentityAndAuthenticationContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(failing);
                var registry = new FailAfterRegisterSessionRegistry(CreateSessionRegistry(failing, repository));
                await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAccountService(
                    failing,
                    repository,
                    registry,
                    passwordHasher).ChangePasswordAsync(new ChangeIdentityPasswordCommand(
                        "User_1",
                        "replacement-secret",
                        "second-replacement",
                        2,
                        new IdentityActorContext("User_1", IdentitySecurityActorKind.IdentityAccount, "trace-rollback"))).AsTask());
            }

            await using (var rollbackVerification = CreateIdentityAndAuthenticationContext(applicationConnectionString))
            {
                var repository = new EfIdentityRepository(rollbackVerification);
                var registry = CreateSessionRegistry(rollbackVerification, repository);
                var account = Assert.IsType<IdentityAccount>(await repository.FindAccountAsync("User_1"));
                var credential = Assert.IsType<LocalCredential>(await repository.FindCredentialAsync("User_1"));
                Assert.Equal(2, account.Revision);
                Assert.NotEqual(PasswordVerificationResult.Failed, passwordHasher.VerifyHashedPassword(account, credential.PasswordHash, "replacement-secret"));
                Assert.Equal(AuthenticationSessionValidationStatus.Active, (await registry.ValidateAsync(changed.Session.Credential)).Status);
                Assert.Equal(2, (await repository.SearchSecurityEventsAsync("User_1")).Count);
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(adminConnectionString, catalog);
        }
    }

    private static EfPlatformDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(options, IdentityModelDefinitions.All);
    }

    private static EfPlatformDbContext CreateIdentityAndAuthorizationContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(
            options,
            IdentityModelDefinitions.All.Concat(AuthorizationModelDefinitions.All).ToArray());
    }

    private static EfPlatformDbContext CreateIdentityAndAuthenticationContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        return new EfPlatformDbContext(
            options,
            IdentityModelDefinitions.All.Concat(AuthenticationModelDefinitions.All).ToArray());
    }

    private static AuthenticationSessionRegistry CreateSessionRegistry(
        EfPlatformDbContext context,
        EfIdentityRepository identityRepository) => new(
            new EfAuthenticationSessionRepository(context),
            new IdentityAccountDirectory(identityRepository),
            new GuidObjectIdGenerator(),
            new DefaultAuthenticationSessionCredentialGenerator(),
            TimeProvider.System,
            new AuthenticationSessionOptions());

    private static IdentityAccountApplicationService CreateAccountService(
        EfPlatformDbContext context,
        EfIdentityRepository repository,
        IAuthenticationSessionRegistry sessionRegistry,
        IPasswordHasher<IdentityAccount> passwordHasher) => new(
            repository,
            new EfPlatformTransactionExecutor(context),
            sessionRegistry,
            passwordHasher,
            new FixedCredentialIssuer(),
            new GuidObjectIdGenerator(),
            TimeProvider.System);

    private static async Task<string> BootstrapAsync(string connectionString, string ecode)
    {
        await using var context = CreateIdentityAndAuthorizationContext(connectionString);
        var repository = new EfIdentityRepository(context);
        var ids = new GuidObjectIdGenerator();
        var service = new IdentityLocalAdministratorMaintenanceService(
            repository,
            new EfPlatformTransactionExecutor(context),
            new NoOpSessionRegistry(),
            new EfLocalAdministratorRoleAssignment(context, ids),
            new PasswordHasher<IdentityAccount>(),
            new FixedCredentialIssuer(),
            ids,
            TimeProvider.System);
        try
        {
            await service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
                ecode,
                "Local Administrator",
                "concurrency test",
                new IdentityActorContext(
                    IdentityMaintenanceActors.LocalMaintenance,
                    IdentitySecurityActorKind.LocalMaintenance,
                    Guid.NewGuid().ToString("N"))));
            return "success";
        }
        catch (IdentityMaintenanceException exception)
        {
            return exception.FailureCode;
        }
    }

    private sealed class FixedCredentialIssuer : ITemporaryCredentialIssuer
    {
        public string Issue() => "sqlserver-temporary-secret";
    }

    private sealed class GuidObjectIdGenerator : IPlatformObjectIdGenerator
    {
        public string NewId(string objectName) => $"{objectName}_{Guid.NewGuid():N}";
    }

    private sealed class NoOpSessionRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credentialDigest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
    }

    private sealed class FailAfterRegisterSessionRegistry(IAuthenticationSessionRegistry inner) : IAuthenticationSessionRegistry
    {
        public async ValueTask<AuthenticationSessionIssuance> RegisterAsync(
            AuthenticationSessionRegistration registration,
            CancellationToken cancellationToken = default)
        {
            _ = await inner.RegisterAsync(registration, cancellationToken);
            throw new InvalidOperationException("injected after session registration");
        }

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) =>
            inner.ValidateAsync(credential, cancellationToken);

        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) =>
            inner.RevokeCurrentAsync(revocation, cancellationToken);

        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) =>
            inner.RevokeAllAsync(revocation, cancellationToken);
    }

    private static IdentityAccount AccountAtRevision(long revision, string name, DateTime updatedAt) => new()
    {
        EID = "User_1",
        ECODE = "alice",
        NormalizedECODE = "ALICE",
        ENAME = name,
        Status = IdentityAccountStatus.Enabled,
        Revision = revision,
        CreatedBy = "actor",
        CreatedAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc),
        UpdatedBy = "actor",
        UpdatedAt = updatedAt
    };

    private static IdentitySecurityEvent Event(string eid, string eventType, DateTime now) => new()
    {
        EID = eid,
        TargetAccountEID = "User_1",
        EventType = eventType,
        Reason = "test reason",
        OccurredAt = now,
        TraceId = "trace",
        ActorKind = IdentitySecurityActorKind.IdentityAccount,
        ActorAccountEID = "User_1",
        CreatedBy = "User_1",
        CreatedAt = now,
        UpdatedBy = "User_1",
        UpdatedAt = now
    };

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
}
