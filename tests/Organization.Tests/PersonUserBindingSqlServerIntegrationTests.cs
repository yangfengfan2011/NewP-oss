using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication.Persistence.Ef;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.EfCore.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ICP.Platform.Testing;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUserBindingSqlServerIntegrationTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION";
    private const string AdminConnectionEnvironmentVariable = "NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION";

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上的 Person/User 绑定生命周期")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_BindingCommitsPersonUserRevisionEventAndAuditOrRollsEverythingBack()
    {
        var fixture = await SqlFixture.CreateAsync("Transaction");
        await using (fixture)
        {
            await using (var setup = CreateRuntimeContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddPerson(setup, "Person_1");
                AddPerson(setup, "Person_2");
                AddAccount(setup, "User_1", 7);
                AddAccount(setup, "User_2", 4);
                AddAccount(setup, "User_Admin", 1);
                await setup.SaveChangesAsync();
            }

            await using (var context = CreateRuntimeContext(fixture.ConnectionString))
            {
                var identityRepository = new EfIdentityRepository(context);
                var participant = new IdentityLifecycleParticipant(
                    identityRepository,
                    new PlatformObjectIdGenerator(),
                    TimeProvider.System);
                var service = new PersonUserBindingService(
                    new EfPersonUserBindingRepository(context),
                    new IdentityAccountDirectory(identityRepository),
                    participant,
                    new EfPlatformTransactionExecutor(context),
                    TimeProvider.System);

                await service.BindAsync(
                    "Person_1",
                    new BindPersonUserRequest("User_1", 7, "SQL Server 正式绑定"),
                    new PersonUserBindingActor("User_Admin", "trace-sql-success"));
            }

            await AssertCommittedAsync(fixture.ConnectionString);

            await using (var context = CreateRuntimeContext(fixture.ConnectionString))
            {
                var identityRepository = new EfIdentityRepository(context);
                var failing = new RegisterThenFailParticipant(new IdentityLifecycleParticipant(
                    identityRepository,
                    new PlatformObjectIdGenerator(),
                    TimeProvider.System));
                var service = new PersonUserBindingService(
                    new EfPersonUserBindingRepository(context),
                    new IdentityAccountDirectory(identityRepository),
                    failing,
                    new EfPlatformTransactionExecutor(context),
                    TimeProvider.System);

                var failure = await Assert.ThrowsAsync<PersonUserBindingException>(async () =>
                    await service.BindAsync(
                        "Person_2",
                        new BindPersonUserRequest("User_2", 4, "SQL Server 故障回滚"),
                        new PersonUserBindingActor("User_Admin", "trace-sql-rollback")));
                Assert.Equal(PersonUserBindingFailureCodes.IdentityUnavailable, failure.FailureCode);
            }

            Assert.Null(await ScalarAsync<string?>(fixture.ConnectionString, "SELECT [AccountEID] FROM [Person] WHERE [EID] = N'Person_2'"));
            Assert.Equal(4L, await ScalarAsync<long>(fixture.ConnectionString, "SELECT [Revision] FROM [IdentityAccount] WHERE [EID] = N'User_2'"));
            Assert.Equal(0, await ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [IdentitySecurityEvent] WHERE [TargetAccountEID] = N'User_2'"));
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上的 Person/User 绑定生命周期")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_LifecycleCommitsAtomicallyAndRestoreNeverRevivesOldSession()
    {
        var fixture = await SqlFixture.CreateAsync("Lifecycle");
        await using (fixture)
        {
            string credential;
            await using (var setup = CreateRuntimeContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddPerson(setup, "Person_1", "User_1", revision: 4);
                AddPerson(setup, "Person_2", "User_2", revision: 4);
                AddAccount(setup, "User_1", 7, isPersonnelBound: true);
                AddAccount(setup, "User_2", 7, isPersonnelBound: true);
                AddAccount(setup, "User_Admin", 1);
                await setup.SaveChangesAsync();

                var sessions = SessionRegistry(setup);
                credential = (await new EfPlatformTransactionExecutor(setup).ExecuteAsync(
                    token => sessions.RegisterAsync(new AuthenticationSessionRegistration
                    {
                        AccountEid = "User_1",
                        Type = AuthenticationSessionType.Regular,
                        ActorId = "User_1"
                    }, token))).Credential;
            }

            await using (var context = CreateRuntimeContext(fixture.ConnectionString))
            {
                var identityRepository = new EfIdentityRepository(context);
                var sessions = SessionRegistry(context);
                var service = new PersonLifecycleService(
                    new EfPersonUserBindingRepository(context),
                    new IdentityAccountDirectory(identityRepository),
                    new IdentityLifecycleParticipant(identityRepository, new PlatformObjectIdGenerator(), TimeProvider.System),
                    sessions,
                    new EfPlatformTransactionExecutor(context),
                    TimeProvider.System);

                var disabled = await service.SetStatusAsync(
                    "Person_1",
                    new SetPersonLifecycleStatusRequest(false, 4, "SQL Server 人员停用"),
                    new PersonLifecycleActor("User_Admin", "trace-sql-disable"));
                Assert.Equal(5, disabled.Revision);
                Assert.Equal(AuthenticationSessionValidationStatus.Revoked, (await sessions.ValidateAsync(credential)).Status);
            }

            Assert.False(await ScalarAsync<bool>(fixture.ConnectionString, "SELECT [IsEnabled] FROM [Person] WHERE [EID] = N'Person_1'"));
            Assert.Equal(5L, await ScalarAsync<long>(fixture.ConnectionString, "SELECT [Revision] FROM [Person] WHERE [EID] = N'Person_1'"));
            Assert.Equal("Disabled", await ScalarAsync<string>(fixture.ConnectionString, "SELECT [Status] FROM [IdentityAccount] WHERE [EID] = N'User_1'"));
            Assert.True(await ScalarAsync<bool>(fixture.ConnectionString, "SELECT [PersonnelDisableHold] FROM [IdentityAccount] WHERE [EID] = N'User_1'"));
            Assert.Equal(1, await ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [AuthenticationSession] WHERE [AccountEID] = N'User_1' AND [RevokedAt] IS NOT NULL"));
            Assert.Equal(1, await ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [IdentitySecurityEvent] WHERE [TargetAccountEID] = N'User_1' AND [EventType] = N'PersonnelDisabled'"));

            await using (var context = CreateRuntimeContext(fixture.ConnectionString))
            {
                var identityRepository = new EfIdentityRepository(context);
                var sessions = SessionRegistry(context);
                var service = new PersonLifecycleService(
                    new EfPersonUserBindingRepository(context),
                    new IdentityAccountDirectory(identityRepository),
                    new IdentityLifecycleParticipant(identityRepository, new PlatformObjectIdGenerator(), TimeProvider.System),
                    sessions,
                    new EfPlatformTransactionExecutor(context),
                    TimeProvider.System);

                var restored = await service.SetStatusAsync(
                    "Person_1",
                    new SetPersonLifecycleStatusRequest(true, 5, "SQL Server 人员恢复"),
                    new PersonLifecycleActor("User_Admin", "trace-sql-restore"));
                Assert.Equal("Disabled", restored.Account?.Status);
                Assert.False(restored.Account?.PersonnelDisableHold);
                Assert.Equal(AuthenticationSessionValidationStatus.Revoked, (await sessions.ValidateAsync(credential)).Status);
            }

            await using (var context = CreateRuntimeContext(fixture.ConnectionString))
            {
                var identityRepository = new EfIdentityRepository(context);
                var sessions = SessionRegistry(context);
                var failing = new RegisterThenFailLifecycleParticipant(new IdentityLifecycleParticipant(
                    identityRepository,
                    new PlatformObjectIdGenerator(),
                    TimeProvider.System));
                var service = new PersonLifecycleService(
                    new EfPersonUserBindingRepository(context),
                    new IdentityAccountDirectory(identityRepository),
                    failing,
                    sessions,
                    new EfPlatformTransactionExecutor(context),
                    TimeProvider.System);

                var failure = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
                    await service.SetStatusAsync(
                        "Person_2",
                        new SetPersonLifecycleStatusRequest(false, 4, "SQL Server 故障回滚"),
                        new PersonLifecycleActor("User_Admin", "trace-sql-rollback")));
                Assert.Equal(PersonLifecycleFailureCodes.DependencyUnavailable, failure.FailureCode);
            }

            Assert.True(await ScalarAsync<bool>(fixture.ConnectionString, "SELECT [IsEnabled] FROM [Person] WHERE [EID] = N'Person_2'"));
            Assert.Equal(4L, await ScalarAsync<long>(fixture.ConnectionString, "SELECT [Revision] FROM [Person] WHERE [EID] = N'Person_2'"));
            Assert.Equal("Enabled", await ScalarAsync<string>(fixture.ConnectionString, "SELECT [Status] FROM [IdentityAccount] WHERE [EID] = N'User_2'"));
            Assert.False(await ScalarAsync<bool>(fixture.ConnectionString, "SELECT [PersonnelDisableHold] FROM [IdentityAccount] WHERE [EID] = N'User_2'"));
            Assert.Equal(0, await ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [IdentitySecurityEvent] WHERE [TargetAccountEID] = N'User_2'"));
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上的 Person/User 绑定生命周期")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task SqlServer_ConcurrentBindingAndConditionalDeleteNeverDeletesBoundPerson()
    {
        var fixture = await SqlFixture.CreateAsync("DeleteRace");
        await using (fixture)
        {
            await using (var setup = CreateRuntimeContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddPerson(setup, "Person_Race");
                AddAccount(setup, "User_Race", 1);
                await setup.SaveChangesAsync();
            }

            using var start = new Barrier(2);
            var bindTask = Task.Run(async () =>
            {
                await using var context = CreateRuntimeContext(fixture.ConnectionString);
                var repository = new EfPersonUserBindingRepository(context);
                start.SignalAndWait();
                return await new EfPlatformTransactionExecutor(context).ExecuteAsync(
                    token => repository.TryBindAsync(
                        "Person_Race",
                        "User_Race",
                        DateTime.UtcNow,
                        "User_Admin",
                        "并发绑定",
                        token));
            });
            var deleteTask = Task.Run(async () =>
            {
                await using var context = CreateRuntimeContext(fixture.ConnectionString);
                var repository = new EfPersonUserBindingRepository(context);
                var executor = new PersonCrudDeleteGuard(repository, context);
                start.SignalAndWait();
                return await executor.DeleteAsync(PersonCrudDescriptorFactory.Create(), "Person_Race");
            });

            var bound = await bindTask;
            var delete = await deleteTask;
            var count = await ScalarAsync<int>(
                fixture.ConnectionString,
                "SELECT COUNT(*) FROM [Person] WHERE [EID] = N'Person_Race';");

            if (bound)
            {
                Assert.Equal(1, count);
                Assert.Equal("User_Race", await ScalarAsync<string>(
                    fixture.ConnectionString,
                    "SELECT [AccountEID] FROM [Person] WHERE [EID] = N'Person_Race';"));
                Assert.False(delete.Succeeded);
                Assert.Equal("Organization.Person.DeleteBlockedByUserBinding", delete.Error?.Code);
            }
            else
            {
                Assert.Equal(0, count);
                Assert.True(delete.Succeeded);
            }
        }
    }

    private static EfPlatformDbContext CreateRuntimeContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        var definitions = PlatformPersistenceModelCatalog.AdaptPlatformCore(NonSqlPlatformBaselineModelSource.GetDefinitions())
            .Concat(IdentityModelDefinitions.All)
            .Concat(AuthenticationModelDefinitions.All)
            .ToArray();
        return new EfPlatformDbContext(options, definitions);
    }

    private static void AddPerson(
        EfPlatformDbContext context,
        string eid,
        string? accountEid = null,
        long revision = 0,
        bool isEnabled = true) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person")).Add(new Dictionary<string, object?>
        {
            ["EID"] = eid,
            ["PersonNo"] = eid,
            ["ENAME"] = eid == "Person_1" ? "张三" : "李四",
            ["CompanyId"] = null,
            ["DepartmentId"] = null,
            ["PositionId"] = null,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = isEnabled,
            ["Revision"] = revision,
            ["AccountEID"] = accountEid,
            ["AccountBoundAt"] = accountEid is null ? null : DateTime.UtcNow,
            ["AccountBoundBy"] = accountEid is null ? null : "User_Admin",
            ["AccountBindingReason"] = accountEid is null ? null : "SQL Server 绑定",
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private static void AddAccount(
        EfPlatformDbContext context,
        string eid,
        long revision,
        bool isPersonnelBound = false,
        bool personnelDisableHold = false,
        string status = "Enabled") =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Identity/IdentityAccount")).Add(new Dictionary<string, object?>
        {
            ["EID"] = eid,
            ["ECODE"] = eid.ToLowerInvariant(),
            ["NormalizedECODE"] = eid.ToUpperInvariant(),
            ["ENAME"] = eid,
            ["Status"] = status,
            ["IsLocalAdministrator"] = false,
            ["IsPersonnelBound"] = isPersonnelBound,
            ["PersonnelDisableHold"] = personnelDisableHold,
            ["Revision"] = revision,
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private static async Task AssertCommittedAsync(string connectionString)
    {
        Assert.Equal("User_1", await ScalarAsync<string>(connectionString, "SELECT [AccountEID] FROM [Person] WHERE [EID] = N'Person_1'"));
        Assert.Equal(1L, await ScalarAsync<long>(connectionString, "SELECT [Revision] FROM [Person] WHERE [EID] = N'Person_1'"));
        Assert.Equal("User_Admin", await ScalarAsync<string>(connectionString, "SELECT [AccountBoundBy] FROM [Person] WHERE [EID] = N'Person_1'"));
        Assert.Equal(8L, await ScalarAsync<long>(connectionString, "SELECT [Revision] FROM [IdentityAccount] WHERE [EID] = N'User_1'"));
        Assert.True(await ScalarAsync<bool>(connectionString, "SELECT [IsPersonnelBound] FROM [IdentityAccount] WHERE [EID] = N'User_1'"));
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM [IdentitySecurityEvent] WHERE [TargetAccountEID] = N'User_1' AND [EventType] = N'PersonnelBound'"));
        Assert.Equal("User_Admin", await ScalarAsync<string>(connectionString, "SELECT [UpdatedBy] FROM [IdentityAccount] WHERE [EID] = N'User_1'"));
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default!;
        return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class RegisterThenFailParticipant(IIdentityLifecycleParticipant inner) : IIdentityLifecycleParticipant
    {
        public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(PersonnelBindingRegistration registration, CancellationToken cancellationToken = default)
        {
            _ = await inner.RegisterPersonnelBindingAsync(registration, cancellationToken);
            throw new InvalidOperationException("failure-after-identity-registration");
        }

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(PersonnelAccountDisableRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(PersonnelDisableHoldReleaseRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RegisterThenFailLifecycleParticipant(IIdentityLifecycleParticipant inner) : IIdentityLifecycleParticipant
    {
        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(PersonnelBindingRegistration registration, CancellationToken cancellationToken = default) =>
            inner.RegisterPersonnelBindingAsync(registration, cancellationToken);

        public async ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(PersonnelAccountDisableRegistration registration, CancellationToken cancellationToken = default)
        {
            _ = await inner.RegisterPersonnelDisableAsync(registration, cancellationToken);
            throw new InvalidOperationException("failure-after-lifecycle-registration");
        }

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(PersonnelDisableHoldReleaseRegistration registration, CancellationToken cancellationToken = default) =>
            inner.RegisterPersonnelDisableHoldReleaseAsync(registration, cancellationToken);
    }

    private static AuthenticationSessionRegistry SessionRegistry(EfPlatformDbContext context) => new(
        new EfAuthenticationSessionRepository(context),
        new IdentityAccountDirectory(new EfIdentityRepository(context)),
        new PlatformObjectIdGenerator(),
        new DefaultAuthenticationSessionCredentialGenerator(),
        TimeProvider.System,
        new AuthenticationSessionOptions());

    private sealed class UnusedSessionRegistry : IAuthenticationSessionRegistry
    {
        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SqlFixture(string adminConnectionString, string catalog, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<SqlFixture> CreateAsync(string suffix)
        {
            var configured = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
            var admin = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
            var catalog = $"NewPOrganization39{suffix}_{Guid.NewGuid():N}";
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
            return new SqlFixture(admin, catalog, new SqlConnectionStringBuilder(admin) { InitialCatalog = catalog }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{catalog}') IS NOT NULL BEGIN ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }
}
