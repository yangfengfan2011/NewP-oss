using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using ICP.Platform.Testing;

namespace ICP.Platform.Persistence.Tests;

[Collection(SqlServerPersistenceSmokeCollection.Name)]
public sealed class SqlServerPersistenceSmokeTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_SQLSERVER_SMOKE";
    private const string AdminConnectionEnvironmentVariable = "NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION";
    private const string ModuleMigrationOne = "20260813001000_CreateAssetCatalog";
    private const string ModuleMigrationTwo = "20260813002000_AddUniqueAssetCode";
    private const string ModuleSchema = "np_partner_assetcatalog";
    private const string FullUpMigrationOne = "20260813021000_CreateFullUpAsset";
    private const string FullUpMigrationTwo = "20260813022000_TransformFullUpAsset";
    private const string FullUpMigrationThree = "20260813023000_AddUniqueFullUpAssetCode";
    private const string FullUpSchema = "np_partner_fullup";
    private const string CompiledFullUpMigrationOne = "20260813031000_CreateCompiledFullUpAsset";
    private const string CompiledFullUpMigrationTwo = "20260813032000_TransformCompiledFullUpAsset";
    private const string ExampleRecordSourceNullableMigration = "20260828082341_Example_000003_RecordSourceNullable";
    private const string ExampleRecordSourceBackfillMigration = "20260828082358_Example_000004_RecordSourceBackfill";
    private const string ExampleRecordSourceRequiredMigration = "20260828082459_Example_000005_RecordSourceRequired";
    private readonly ITestOutputHelper testOutput;

    public SqlServerPersistenceSmokeTests(ITestOutputHelper testOutput)
    {
        this.testOutput = testOutput;
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_EmptyCatalogCreatesCurrentFoundationAndWarmStartIsNoOp() =>
        RunCatalogScenarioAsync("forward", VerifyForwardMigrationAndWarmNoOpAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerHostBootstrap_EmptyCatalogBecomesReadyAndWarmStartIsNoOp() =>
        RunCatalogScenarioAsync("host", VerifyHostBootstrapAndWarmNoOpAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerReadOnlyConsistency_DriftFailsWithoutRepairOrHistoryWrite() =>
        RunCatalogScenarioAsync("consistency", VerifyReadOnlyConsistencyFailureAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_NonEmptyUnownedCatalogIsRejected() =>
        RunCatalogScenarioAsync("unowned", VerifyNonEmptyUnownedCatalogIsRejectedAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_CatalogFingerprintMismatchIsRejected() =>
        RunCatalogScenarioAsync("mismatch", VerifyCatalogFingerprintMismatchIsRejectedAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_PreviousMigrationHistoryRequiresDatabaseRebuild() =>
        RunCatalogScenarioAsync("oldhistory", VerifyPreviousMigrationHistoryRequiresDatabaseRebuildAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerModuleMigrations_ApplyNoOpFailureRetryAndOldPlanAhead() =>
        RunCatalogScenarioAsync("module", VerifyModuleMigrationLifecycleAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_PrecreatedBusinessTableRequiresDatabaseRebuild() =>
        RunCatalogScenarioAsync("prebuilt", VerifyPrecreatedBusinessTableRequiresDatabaseRebuildAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerProviderTransitions_EnforceDataPreconditionsWithoutDefaultsOrBackfill() =>
        RunCatalogScenarioAsync("target", VerifyProviderTransitionDataBoundariesAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerExampleRequiredField_BackfillsHistoricalPrefixWithoutPermanentDefaultAndRestartsReady() =>
        RunCatalogScenarioAsync("requiredfield", VerifyExampleRequiredFieldLifecycleAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerFullUp_DropRenameAlterRollbackRetryConcurrentAndNoOp() =>
        RunCatalogScenarioAsync("fullup", VerifyFullUpMigrationLifecycleAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的迁移生命周期冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerCompiledHostFullUp_DestructiveCompatibilityRunsWithoutRiskLabelCutoff() =>
        RunCatalogScenarioAsync("compiledfullup", VerifyCompiledHostFullUpAsync);

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server，一次性 catalog 上的部门层级约束冒烟")]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerOrganizationDepartmentHierarchy_EnforcesSiblingUniquenessAndCompanyScopedParent() =>
        RunCatalogScenarioAsync("departmenthierarchy", VerifyOrganizationDepartmentHierarchyAsync);

    private async Task RunCatalogScenarioAsync(
        string purpose,
        Func<string, string, WorktreeDatabaseIdentity, Task> scenario)
    {
        var repositoryRoot = FindRepositoryRoot();
        var identity = WorktreeDatabaseIdentity.Resolve(repositoryRoot);
        var adminConnectionString = ResolveAdminConnectionString();
        string? catalog = null;

        try
        {
            catalog = await CreateCatalogAsync(adminConnectionString, identity, purpose);
            testOutput.WriteLine(
                "NEWP_SQLSERVER_SMOKE purpose={0} catalog={1} checkpoint=catalog-created",
                purpose,
                catalog);
            await scenario(adminConnectionString, catalog, identity);
            testOutput.WriteLine(
                "NEWP_SQLSERVER_SMOKE purpose={0} catalog={1} checkpoint=scenario-passed",
                purpose,
                catalog);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (catalog is not null)
            {
                await DropCreatedCatalogAsync(adminConnectionString, identity, catalog);
                testOutput.WriteLine(
                    "NEWP_SQLSERVER_SMOKE purpose={0} catalog={1} cleanup=catalog-dropped",
                    purpose,
                    catalog);
            }
        }
    }

    private static async Task VerifyForwardMigrationAndWarmNoOpAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using var provider = BuildProvider(
            applicationConnectionString,
            identity,
            TestPersistencePlan.CreateCurrentSqlServer());
        var expectedHistory = ReadCanonicalHistory(provider);
        var expectedMigrationIds = expectedHistory
            .Select(static entry => entry.MigrationId)
            .ToArray();

        Assert.False(await RunMigrationsAsync(provider));

        await using (var scope = provider.CreateAsyncScope())
        {
            var checker = scope.ServiceProvider.GetRequiredService<IPersistenceConsistencyChecker>();
            await checker.CheckAsync();
        }

        var historyBeforeWarmStart = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(expectedMigrationIds, historyBeforeWarmStart);
        foreach (var table in new[]
        {
            "ConfigItem", "ValueListItem", "Role", "Permission", "RolePermission", "UserRole",
            "IdentityAccount", "LocalCredential", "IdentitySecurityEvent", "AuthenticationSession",
            "Company", "Department", "Person", "Position"
        })
        {
            Assert.True(await TableExistsAsync(applicationConnectionString, table), table);
        }
        Assert.Equal(0, await CountBusinessDefaultConstraintsAsync(applicationConnectionString));
        Assert.Equal(0, await CountNullableAuditColumnsAsync(applicationConnectionString));
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(applicationConnectionString));

        Assert.False(await RunMigrationsAsync(provider));

        var historyAfterWarmStart = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(historyBeforeWarmStart, historyAfterWarmStart);
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(applicationConnectionString));

        await ExecuteNonQueryAsync(applicationConnectionString, """
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'20990101000000_FutureMigration', N'99.0.0');
            """);
        var ahead = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(provider));
        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, ahead.FailureCode);
        Assert.Equal(
            "20990101000000_FutureMigration",
            (await ReadAppliedMigrationsAsync(applicationConnectionString))[^1]);

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            applicationConnectionString,
            "UPDATE [dbo].[__NewPDatabaseOwnership] SET [Version] = 2 WHERE [MarkerId] = 1;"));
        Assert.Equal(51000, exception.Number);
    }

    private static async Task VerifyHostBootstrapAndWarmNoOpAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        var configuration = BuildSqlServerConfiguration(applicationConnectionString);
        Assert.Equal(identity, WorktreeDatabaseIdentity.Resolve(FindRepositoryRoot()));
        IReadOnlyList<CompiledMigrationHistoryEntry> expectedHistory;
        await using (var app = BuildHostApplication(configuration, "Production"))
        {
            expectedHistory = ReadCanonicalHistory(app.Services);
            await app.ApplyPlatformPersistenceAsync();
            var readiness = await ReadPersistenceReadinessAsync(app);
            Assert.Equal(ReadinessState.Ready, readiness.State);
            Assert.Null(readiness.FailureCode);
        }

        var historyBeforeWarmStart = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(
            expectedHistory.Select(static entry => entry.MigrationId),
            historyBeforeWarmStart);
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(applicationConnectionString));

        await using (var restarted = BuildHostApplication(configuration, "Development"))
        {
            await restarted.ApplyPlatformPersistenceAsync();
            var readiness = await ReadPersistenceReadinessAsync(restarted);
            Assert.Equal(ReadinessState.Ready, readiness.State);
        }
        Assert.Equal(historyBeforeWarmStart, await ReadAppliedMigrationsAsync(applicationConnectionString));
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(applicationConnectionString));
    }

    private static WebApplication BuildHostApplication(
        IConfiguration configuration,
        string environmentName,
        CompiledPersistencePlan? plan = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = FindRepositoryRoot()
        });
        builder.Services.AddPlatformPersistence(
            configuration,
            plan ?? TestPersistencePlan.CreateCurrentSqlServer());
        return builder.Build();
    }

    private static async Task<ReadinessContribution> ReadPersistenceReadinessAsync(WebApplication app)
    {
        var contributor = new PersistenceReadinessContributor(
            app.Services.GetRequiredService<PersistenceReadinessState>());
        return await contributor.CheckAsync(CancellationToken.None);
    }

    private static async Task VerifyReadOnlyConsistencyFailureAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        Assert.Equal(identity, WorktreeDatabaseIdentity.Resolve(FindRepositoryRoot()));
        await using var app = BuildHostApplication(
            BuildSqlServerConfiguration(applicationConnectionString),
            "Acceptance");
        await app.ApplyPlatformPersistenceAsync();
        var historyBefore = await ReadAppliedMigrationsAsync(applicationConnectionString);
        var factsBefore = await CountMigrationFactsAsync(applicationConnectionString);

        await ExecuteNonQueryAsync(applicationConnectionString, """
            CREATE TABLE [dbo].[UnexpectedDrift]
            (
                [Id] int NOT NULL CONSTRAINT [PK_UnexpectedDrift] PRIMARY KEY
            );
            INSERT INTO [dbo].[UnexpectedDrift] ([Id]) VALUES (1);
            """);

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            app.ApplyPlatformPersistenceAsync());
        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, exception.FailureCode);
        var readiness = await ReadPersistenceReadinessAsync(app);
        Assert.Equal(ReadinessState.Failed, readiness.State);
        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, readiness.FailureCode);

        Assert.True(await TableExistsAsync(applicationConnectionString, "UnexpectedDrift"));
        Assert.Equal(
            1,
            Convert.ToInt32(await ExecuteScalarAsync(
                applicationConnectionString,
                "SELECT COUNT(*) FROM [dbo].[UnexpectedDrift] WHERE [Id] = 1;")));
        Assert.Equal(historyBefore, await ReadAppliedMigrationsAsync(applicationConnectionString));
        Assert.Equal(factsBefore, await CountMigrationFactsAsync(applicationConnectionString));
    }

    private static async Task VerifyPrecreatedBusinessTableRequiresDatabaseRebuildAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await ExecuteNonQueryAsync(applicationConnectionString, """
            CREATE TABLE [dbo].[Person] ([EID] nvarchar(128) NOT NULL PRIMARY KEY);
            """);
        await using var provider = BuildProvider(
            applicationConnectionString,
            identity,
            TestPersistencePlan.CreateCurrentSqlServer());
        var exception = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(provider));
        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired, exception.FailureCode);
        Assert.True(await TableExistsAsync(applicationConnectionString, "Person"));
        Assert.False(await TableExistsAsync(applicationConnectionString, "Role"));
        Assert.False(await TableExistsAsync(applicationConnectionString, "__EFMigrationsHistory"));
    }

    private static async Task VerifyProviderTransitionDataBoundariesAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var connectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using (var initial = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(1)))
        {
            Assert.False(await RunMigrationsAsync(initial));
        }

        await ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [np_partner_transition].[TransitionAsset] ([Id], [OptionalValue])
            VALUES (N'old-row', NULL);
            """);

        await using (var nullable = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(2)))
        {
            Assert.False(await RunMigrationsAsync(nullable));
        }
        Assert.Null(await ExecuteScalarAsync(
            connectionString,
            "SELECT [OptionalAdded] FROM [np_partner_transition].[TransitionAsset] WHERE [Id] = N'old-row';"));
        Assert.False(await DefaultConstraintExistsAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "OptionalAdded"));

        await using (var required = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(3)))
        {
            var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => RunMigrationsAsync(required));
            Assert.Equal(PersistenceMigrationFailureCodes.TableNotEmpty, exception.FailureCode);
        }
        Assert.False(await ColumnExistsAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "RequiredAdded"));
        Assert.DoesNotContain(
            AddRequiredTransitionColumnMigration.MigrationId,
            await ReadAppliedMigrationsAsync(connectionString));

        await ExecuteNonQueryAsync(
            connectionString,
            "DELETE FROM [np_partner_transition].[TransitionAsset];");
        await using (var required = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(3)))
        {
            Assert.False(await RunMigrationsAsync(required));
        }
        Assert.True(await ColumnExistsAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "RequiredAdded"));
        Assert.False(await DefaultConstraintExistsAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "RequiredAdded"));

        await ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [np_partner_transition].[TransitionAsset]
                ([Id], [OptionalValue], [OptionalAdded], [RequiredAdded])
            VALUES
                (N'nullable-row', NULL, NULL, 7);
            """);
        await using (var requiredValue = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(4)))
        {
            var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => RunMigrationsAsync(requiredValue));
            Assert.True(
                string.Equals(
                    PersistenceMigrationFailureCodes.NullValuesPresent,
                    exception.FailureCode,
                    StringComparison.Ordinal),
                exception.ToString());
        }
        Assert.True(await ColumnIsNullableAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "OptionalValue"));
        Assert.DoesNotContain(
            RequireTransitionValueMigration.MigrationId,
            await ReadAppliedMigrationsAsync(connectionString));

        await ExecuteNonQueryAsync(
            connectionString,
            "UPDATE [np_partner_transition].[TransitionAsset] SET [OptionalValue] = N'provided' WHERE [Id] = N'nullable-row';");
        await using (var requiredValue = BuildProvider(connectionString, identity, BuildProviderTransitionPlan(4)))
        {
            Assert.False(await RunMigrationsAsync(requiredValue));
        }
        Assert.False(await ColumnIsNullableAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "OptionalValue"));
        Assert.False(await DefaultConstraintExistsAsync(
            connectionString,
            "np_partner_transition",
            "TransitionAsset",
            "OptionalValue"));
    }

    private static async Task VerifyExampleRequiredFieldLifecycleAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var connectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        var configuration = BuildSqlServerConfiguration(connectionString);
        Assert.Equal(identity, WorktreeDatabaseIdentity.Resolve(FindRepositoryRoot()));

        await using (var historicalHost = BuildHostApplication(
            configuration,
            "Acceptance",
            BuildExampleMigrationPlan(2)))
        {
            await historicalHost.ApplyPlatformPersistenceAsync();
            Assert.Equal(ReadinessState.Ready, (await ReadPersistenceReadinessAsync(historicalHost)).State);
        }

        await ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[ExampleRecord]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [ReviewerUserEID], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'legacy-row', SYSUTCDATETIME(), N'smoke', N'legacy-code', N'Legacy row', NULL, SYSUTCDATETIME(), N'smoke');
            """);

        await using (var nullableHost = BuildHostApplication(
            configuration,
            "Acceptance",
            BuildExampleMigrationPlan(3)))
        {
            await nullableHost.ApplyPlatformPersistenceAsync();
            Assert.Equal(ReadinessState.Ready, (await ReadPersistenceReadinessAsync(nullableHost)).State);
        }

        Assert.Null(await ExecuteScalarAsync(
            connectionString,
            "SELECT [RecordSource] FROM [dbo].[ExampleRecord] WHERE [EID] = N'legacy-row';"));
        Assert.True(await ColumnIsNullableAsync(connectionString, "dbo", "ExampleRecord", "RecordSource"));
        Assert.False(await DefaultConstraintExistsAsync(connectionString, "dbo", "ExampleRecord", "RecordSource"));

        await ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[ExampleRecord]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [ReviewerUserEID], [RecordSource], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'manual-row', SYSUTCDATETIME(), N'smoke', N'manual-code', N'Manual row', NULL, N'Manual', SYSUTCDATETIME(), N'smoke');
            """);

        IReadOnlyList<CompiledMigrationHistoryEntry> expectedHistory;
        await using (var currentHost = BuildHostApplication(configuration, "Production"))
        {
            expectedHistory = ReadCanonicalHistory(currentHost.Services);
            await currentHost.ApplyPlatformPersistenceAsync();
            var readiness = await ReadPersistenceReadinessAsync(currentHost);
            Assert.Equal(ReadinessState.Ready, readiness.State);
            Assert.Null(readiness.FailureCode);
        }

        Assert.Equal(
            "Legacy",
            await ExecuteScalarAsync(
                connectionString,
                "SELECT [RecordSource] FROM [dbo].[ExampleRecord] WHERE [EID] = N'legacy-row';"));
        Assert.Equal(
            "Manual",
            await ExecuteScalarAsync(
                connectionString,
                "SELECT [RecordSource] FROM [dbo].[ExampleRecord] WHERE [EID] = N'manual-row';"));
        Assert.False(await ColumnIsNullableAsync(connectionString, "dbo", "ExampleRecord", "RecordSource"));
        Assert.False(await DefaultConstraintExistsAsync(connectionString, "dbo", "ExampleRecord", "RecordSource"));

        var historyBeforeRestart = await ReadAppliedMigrationsAsync(connectionString);
        Assert.Equal(expectedHistory.Select(static entry => entry.MigrationId), historyBeforeRestart);
        Assert.Contains(ExampleRecordSourceNullableMigration, historyBeforeRestart);
        Assert.Contains(ExampleRecordSourceBackfillMigration, historyBeforeRestart);
        Assert.Contains(ExampleRecordSourceRequiredMigration, historyBeforeRestart);
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(connectionString));

        await using (var restartedHost = BuildHostApplication(configuration, "Development"))
        {
            await restartedHost.ApplyPlatformPersistenceAsync();
            Assert.Equal(ReadinessState.Ready, (await ReadPersistenceReadinessAsync(restartedHost)).State);
        }

        Assert.Equal(historyBeforeRestart, await ReadAppliedMigrationsAsync(connectionString));
        Assert.Equal(expectedHistory.Count, await CountMigrationFactsAsync(connectionString));
    }

    private static async Task VerifyFullUpMigrationLifecycleAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var connectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using (var initial = BuildProvider(connectionString, identity, BuildFullUpPlan(1)))
        {
            Assert.False(await RunMigrationsAsync(initial));
        }

        await ExecuteNonQueryAsync(connectionString, $"""
            INSERT INTO [{FullUpSchema}].[Parent] ([Id]) VALUES (N'parent-1');
            INSERT INTO [{FullUpSchema}].[LegacyAsset] ([Id], [LegacyCode], [ParentId], [Obsolete])
            VALUES (N'asset-1', N'FULL-UP', N'parent-1', N'remove-me');
            INSERT INTO [{FullUpSchema}].[RetiredAsset] ([Id]) VALUES (N'retired-1');
            """);

        await using var concurrentOne = BuildProvider(connectionString, identity, BuildFullUpPlan(2));
        await using var concurrentTwo = BuildProvider(connectionString, identity, BuildFullUpPlan(2));
        var concurrentResults = await Task.WhenAll(
            RunMigrationsAsync(concurrentOne),
            RunMigrationsAsync(concurrentTwo));
        Assert.All(concurrentResults, static databaseAhead => Assert.False(databaseAhead));

        Assert.False(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "LegacyAsset", "U"));
        Assert.True(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "Asset", "U"));
        Assert.False(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "RetiredAsset", "U"));
        Assert.False(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "LegacyCode"));
        Assert.True(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.False(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "Obsolete"));
        Assert.False(await ColumnIsNullableAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.Equal(
            256,
            await ReadCharacterMaximumLengthAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.Equal(
            "FULL-UP",
            await ExecuteScalarAsync(
                connectionString,
                $"SELECT [Code] FROM [{FullUpSchema}].[Asset] WHERE [Id] = N'asset-1';"));
        Assert.False(await DefaultConstraintExistsAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.True(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "PK_Asset", "PK"));
        Assert.True(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "FK_Asset_Parent", "F"));
        Assert.True(await IndexExistsAsync(connectionString, FullUpSchema, "Asset", "IX_Asset_Code"));
        Assert.True(await IndexExistsAsync(connectionString, FullUpSchema, "Asset", "UX_Asset_ParentId"));
        Assert.True(await IndexIsUniqueAsync(connectionString, FullUpSchema, "Asset", "UX_Asset_ParentId"));
        Assert.Equal(
            1,
            Convert.ToInt32(await ExecuteScalarAsync(
                connectionString,
                $"SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory] WHERE [MigrationId] = N'{FullUpMigrationTwo}';")));

        await ExecuteNonQueryAsync(connectionString, $"""
            INSERT INTO [{FullUpSchema}].[Parent] ([Id]) VALUES (N'parent-2');
            INSERT INTO [{FullUpSchema}].[Asset] ([Id], [Code], [ParentId])
            VALUES (N'asset-2', N'FULL-UP', N'parent-2');
            """);

        await using var failing = BuildProvider(connectionString, identity, BuildFullUpPlan(3));
        await Assert.ThrowsAsync<SqlException>(() => RunMigrationsAsync(failing));
        Assert.DoesNotContain(FullUpMigrationThree, await ReadAppliedMigrationsAsync(connectionString));
        Assert.True(await IndexExistsAsync(connectionString, FullUpSchema, "Asset", "IX_Asset_Code"));
        Assert.False(await IndexExistsAsync(connectionString, FullUpSchema, "Asset", "UX_Asset_Code"));

        await ExecuteNonQueryAsync(
            connectionString,
            $"DELETE FROM [{FullUpSchema}].[Asset] WHERE [Id] = N'asset-2';");
        Assert.False(await RunMigrationsAsync(failing));
        Assert.False(await IndexExistsAsync(connectionString, FullUpSchema, "Asset", "IX_Asset_Code"));
        Assert.True(await IndexIsUniqueAsync(connectionString, FullUpSchema, "Asset", "UX_Asset_Code"));

        var completedHistory = await ReadAppliedMigrationsAsync(connectionString);
        Assert.Equal(1, completedHistory.Count(migration => migration == FullUpMigrationThree));
        Assert.False(await RunMigrationsAsync(failing));
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(connectionString));
        Assert.True(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "Asset", "U"));
        Assert.False(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "LegacyAsset", "U"));
    }

    private static async Task VerifyCompiledHostFullUpAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var connectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        using var initialFixture = BuildCompiledFullUpPlan(stage: 1);
        await using (var initial = BuildHostApplication(
                         BuildSqlServerConfiguration(connectionString),
                         "Production",
                         initialFixture.Plan))
        {
            await initial.ApplyPlatformPersistenceAsync();
            Assert.Equal(ReadinessState.Ready, (await ReadPersistenceReadinessAsync(initial)).State);
        }

        await ExecuteNonQueryAsync(connectionString, $"""
            INSERT INTO [{FullUpSchema}].[LegacyAsset]
                ([EID], [ALegacyCode], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [ZObsolete])
            VALUES
                (N'asset-1', N'COMPILED-UP', SYSUTCDATETIME(), N'test', SYSUTCDATETIME(), N'test', N'remove-me');
            """);

        using var destructiveFixture = BuildCompiledFullUpPlan(stage: 2);
        var compiledStream = Assert.Single(
            destructiveFixture.Plan.Streams,
            static stream => stream.ModuleId == "Partner.FullUp");
        Assert.Equal(
            ["safe-expand", "backup-restore-only"],
            compiledStream.Migrations.Select(static migration => migration.Compatibility));
        await using (var destructive = BuildHostApplication(
                         BuildSqlServerConfiguration(connectionString),
                         "Acceptance",
                         destructiveFixture.Plan))
        {
            await destructive.ApplyPlatformPersistenceAsync();
            Assert.Equal(ReadinessState.Ready, (await ReadPersistenceReadinessAsync(destructive)).State);
        }

        Assert.False(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "LegacyAsset", "U"));
        Assert.True(await SchemaObjectExistsAsync(connectionString, FullUpSchema, "Asset", "U"));
        Assert.False(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "ALegacyCode"));
        Assert.True(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.False(await ColumnExistsAsync(connectionString, FullUpSchema, "Asset", "ZObsolete"));
        Assert.False(await ColumnIsNullableAsync(connectionString, FullUpSchema, "Asset", "Code"));
        Assert.Equal(
            "COMPILED-UP",
            await ExecuteScalarAsync(
                connectionString,
                $"SELECT [Code] FROM [{FullUpSchema}].[Asset] WHERE [EID] = N'asset-1';"));
        var appliedMigrations = await ReadAppliedMigrationsAsync(connectionString);
        Assert.Contains(CompiledFullUpMigrationOne, appliedMigrations);
        Assert.Contains(CompiledFullUpMigrationTwo, appliedMigrations);
        Assert.Equal(
            1,
            Convert.ToInt32(await ExecuteScalarAsync(
                connectionString,
                $"SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory] WHERE [MigrationId] = N'{CompiledFullUpMigrationTwo}';")));
    }

    private static async Task VerifyNonEmptyUnownedCatalogIsRejectedAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "CREATE TABLE [dbo].[ForeignTable] ([Id] int NOT NULL PRIMARY KEY);");
        await using var provider = BuildProvider(applicationConnectionString, identity);

        var exception = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(provider));

        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired, exception.FailureCode);
        Assert.False(await TableExistsAsync(applicationConnectionString, "__NewPDatabaseOwnership"));
        Assert.False(await TableExistsAsync(applicationConnectionString, "__EFMigrationsHistory"));
        Assert.True(await TableExistsAsync(applicationConnectionString, "ForeignTable"));
    }

    private static async Task VerifyOrganizationDepartmentHierarchyAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var connectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using var provider = BuildProvider(
            connectionString,
            identity,
            TestPersistencePlan.CreateCurrentSqlServer());
        Assert.False(await RunMigrationsAsync(provider));

        Assert.Null(await ExecuteScalarAsync(connectionString, """
            SELECT [filter_definition]
            FROM [sys].[indexes]
            WHERE [object_id] = OBJECT_ID(N'[dbo].[Department]')
              AND [name] = N'UX_Department_Company_Parent_ENAME';
            """));

        await ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[Company]
                ([EID], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [IsEnabled], [SortOrder], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'C1', SYSUTCDATETIME(), N'test', N'C1', N'Company 1', 1, N'1', SYSUTCDATETIME(), N'test'),
                (N'C2', SYSUTCDATETIME(), N'test', N'C2', N'Company 2', 1, N'2', SYSUTCDATETIME(), N'test');

            INSERT INTO [dbo].[Department]
                ([EID], [CompanyId], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [IsEnabled], [ParentDepartmentId], [SortOrder], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'P1', N'C1', SYSUTCDATETIME(), N'test', N'P1', N'Parent 1', 1, NULL, N'1', SYSUTCDATETIME(), N'test'),
                (N'P2', N'C1', SYSUTCDATETIME(), N'test', N'P2', N'Parent 2', 1, NULL, N'2', SYSUTCDATETIME(), N'test'),
                (N'R1', N'C1', SYSUTCDATETIME(), N'test', N'R1', N'Root Name', 1, NULL, N'3', SYSUTCDATETIME(), N'test'),
                (N'C1-CHILD', N'C1', SYSUTCDATETIME(), N'test', N'C1-CHILD', N'Child Name', 1, N'P1', N'4', SYSUTCDATETIME(), N'test'),
                (N'C1-OTHER', N'C1', SYSUTCDATETIME(), N'test', N'C1-OTHER', N'Child Name', 1, N'P2', N'5', SYSUTCDATETIME(), N'test');
            """);

        var duplicateRoot = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[Department]
                ([EID], [CompanyId], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [IsEnabled], [ParentDepartmentId], [SortOrder], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'R2', N'C1', SYSUTCDATETIME(), N'test', N'R2', N'root name', 1, NULL, N'6', SYSUTCDATETIME(), N'test');
            """));
        Assert.Contains(duplicateRoot.Errors.Cast<SqlError>(), static error => error.Number is 2601 or 2627);

        var duplicateSibling = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[Department]
                ([EID], [CompanyId], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [IsEnabled], [ParentDepartmentId], [SortOrder], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'C1-DUP', N'C1', SYSUTCDATETIME(), N'test', N'C1-DUP', N'child name', 1, N'P1', N'7', SYSUTCDATETIME(), N'test');
            """));
        Assert.Contains(duplicateSibling.Errors.Cast<SqlError>(), static error => error.Number is 2601 or 2627);

        var crossCompanyParent = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(connectionString, """
            INSERT INTO [dbo].[Department]
                ([EID], [CompanyId], [CreatedAt], [CreatedBy], [ECODE], [ENAME], [IsEnabled], [ParentDepartmentId], [SortOrder], [UpdatedAt], [UpdatedBy])
            VALUES
                (N'C2-CHILD', N'C2', SYSUTCDATETIME(), N'test', N'C2-CHILD', N'Cross Company', 1, N'P1', N'8', SYSUTCDATETIME(), N'test');
            """));
        Assert.Contains(crossCompanyParent.Errors.Cast<SqlError>(), static error => error.Number == 547);
    }

    private static async Task VerifyCatalogFingerprintMismatchIsRejectedAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        var builder = new SqlConnectionStringBuilder(applicationConnectionString);
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            """
            CREATE TABLE [dbo].[__NewPDatabaseOwnership]
            (
                [MarkerId] int NOT NULL PRIMARY KEY,
                [Version] int NOT NULL,
                [RepositoryId] char(64) NOT NULL,
                [WorktreeId] char(64) NOT NULL,
                [Provider] nvarchar(32) NOT NULL,
                [ServerFingerprint] char(64) NOT NULL,
                [CatalogFingerprint] char(64) NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL
            );
            """);

        await using (var connection = new SqlConnection(applicationConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO [dbo].[__NewPDatabaseOwnership]
                    ([MarkerId], [Version], [RepositoryId], [WorktreeId], [Provider], [ServerFingerprint], [CatalogFingerprint], [CreatedAtUtc])
                VALUES
                    (1, 1, @repositoryId, @worktreeId, N'SqlServer', @serverFingerprint, @catalogFingerprint, SYSUTCDATETIME());
                """;
            command.Parameters.AddWithValue("@repositoryId", identity.RepositoryId);
            command.Parameters.AddWithValue("@worktreeId", identity.WorktreeId);
            command.Parameters.AddWithValue("@serverFingerprint", Fingerprint(builder.DataSource));
            command.Parameters.AddWithValue("@catalogFingerprint", new string('0', 64));
            await command.ExecuteNonQueryAsync();
        }

        await using var provider = BuildProvider(applicationConnectionString, identity);
        var exception = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(provider));

        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseIdentityMismatch, exception.FailureCode);
        Assert.False(await TableExistsAsync(applicationConnectionString, "__EFMigrationsHistory"));
    }

    private static async Task VerifyPreviousMigrationHistoryRequiresDatabaseRebuildAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using var provider = BuildProvider(
            applicationConnectionString,
            identity,
            TestPersistencePlan.CreateCurrentSqlServer());

        await using (var scope = provider.CreateAsyncScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationGuard>();
            await guard.VerifyAsync();
        }

        await ExecuteNonQueryAsync(applicationConnectionString, """
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL PRIMARY KEY,
                [ProductVersion] nvarchar(32) NOT NULL
            );
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'20260409154403_InitialCreate', N'10.0.0');
            """);

        var exception = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(provider));

        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseRebuildRequired, exception.FailureCode);
        var history = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(["20260409154403_InitialCreate"], history);
        Assert.False(await TableExistsAsync(applicationConnectionString, "Role"));
        Assert.False(await TableExistsAsync(applicationConnectionString, "__NewPMigrationHistory"));
    }

    private static async Task VerifyModuleMigrationLifecycleAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        var firstPlan = BuildModulePlan(includeSecondMigration: false);
        var secondPlan = BuildModulePlan(includeSecondMigration: true);
        Assert.NotEqual(
            firstPlan.Streams.Single(static stream => stream.ModuleId == "Partner.AssetCatalog").BundleChecksum,
            secondPlan.Streams.Single(static stream => stream.ModuleId == "Partner.AssetCatalog").BundleChecksum);

        using var firstProvider = BuildProvider(applicationConnectionString, identity, firstPlan);
        Assert.False(await RunMigrationsAsync(firstProvider));
        var firstHistory = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(ModuleMigrationOne, firstHistory);
        Assert.DoesNotContain(ModuleMigrationTwo, firstHistory);
        Assert.Equal(
            1,
            Convert.ToInt32(await ExecuteScalarAsync(
                applicationConnectionString,
                $"SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory] WHERE [MigrationId] = N'{ModuleMigrationOne}';")));
        Assert.True(await SchemaObjectExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "U"));
        Assert.False(await SchemaObjectExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "__EFMigrationsHistory",
            "U"));

        Assert.False(await RunMigrationsAsync(firstProvider));
        Assert.Equal(firstHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));

        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            INSERT INTO [{ModuleSchema}].[Asset] ([Id], [Code])
            VALUES (N'asset-1', N'DUPLICATE'), (N'asset-2', N'DUPLICATE');
            """);

        using var secondProvider = BuildProvider(applicationConnectionString, identity, secondPlan);
        await Assert.ThrowsAsync<SqlException>(() => RunMigrationsAsync(secondProvider));

        var failedHistory = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(ModuleMigrationOne, failedHistory);
        Assert.DoesNotContain(ModuleMigrationTwo, failedHistory);
        Assert.Equal(
            0,
            Convert.ToInt32(await ExecuteScalarAsync(
                applicationConnectionString,
                $"SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory] WHERE [MigrationId] = N'{ModuleMigrationTwo}';")));
        Assert.False(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            $"SELECT TOP (0) [Id], [Code] FROM [{ModuleSchema}].[Asset];");

        // 当前 migration 已整体回滚，数据库仍精确位于旧 frontier，旧计划可继续启动。
        Assert.False(await RunMigrationsAsync(firstProvider));
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            DELETE FROM [{ModuleSchema}].[Asset] WHERE [Id] = N'asset-2';
            """);

        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            UPDATE [dbo].[__NewPMigrationHistory]
            SET [ContentDigest] = '{new string('f', 64)}'
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);
        var digestMismatch = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => RunMigrationsAsync(secondProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.ContentMismatch, digestMismatch.FailureCode);
        Assert.False(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            UPDATE [dbo].[__NewPMigrationHistory]
            SET [ContentDigest] = '{new string('1', 64)}'
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);

        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            UPDATE [dbo].[__NewPMigrationHistory]
            SET [BundleDigest] = '{new string('f', 64)}'
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);
        var bundleMismatch = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => RunMigrationsAsync(secondProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.BundleMismatch, bundleMismatch.FailureCode);
        Assert.False(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));
        var firstStream = firstPlan.Streams.Single(static stream => stream.ModuleId == "Partner.AssetCatalog");
        var firstBundleDigest = UnifiedMigrationHistoryDigest.ComputeBundleDigest(
            firstStream.ModuleId,
            firstStream.Provider,
            firstStream.Migrations[0]);
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            UPDATE [dbo].[__NewPMigrationHistory]
            SET [BundleDigest] = '{firstBundleDigest}'
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);

        Assert.False(await RunMigrationsAsync(secondProvider));
        var completedHistory = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(ModuleMigrationTwo, completedHistory);
        Assert.Equal(
            1,
            Convert.ToInt32(await ExecuteScalarAsync(
                applicationConnectionString,
                $"SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory] WHERE [MigrationId] = N'{ModuleMigrationTwo}';")));
        Assert.True(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));

        Assert.False(await RunMigrationsAsync(secondProvider));
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));

        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            DELETE FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);
        var nonPrefix = await Assert.ThrowsAsync<DatabaseOwnershipException>(
            () => RunMigrationsAsync(secondProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.NonPrefix, nonPrefix.FailureCode);

        const string unknownMigration = "20260813001500_UnknownAssetCatalog";
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'{unknownMigration}', N'10.0.0');
            """);
        var unknown = await Assert.ThrowsAsync<DatabaseOwnershipException>(
            () => RunMigrationsAsync(secondProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.Unknown, unknown.FailureCode);
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            DELETE FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'{unknownMigration}';
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'{ModuleMigrationOne}', N'10.0.0');
            """);
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));

        // 新 frontier 生效后，旧计划必须在任何 DDL 前阻断，不能作为兼容快照 Ready。
        var ahead = await Assert.ThrowsAsync<DatabaseOwnershipException>(() => RunMigrationsAsync(firstProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.FrontierMismatch, ahead.FailureCode);
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            $"SELECT TOP (0) [Id], [Code] FROM [{ModuleSchema}].[Asset];");
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));

        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            DELETE FROM [dbo].[__NewPMigrationHistory]
            WHERE [MigrationId] = N'{ModuleMigrationOne}';
            """);
        var factMissing = await Assert.ThrowsAsync<PersistencePlanExecutionException>(
            () => RunMigrationsAsync(secondProvider));
        Assert.Equal(PersistenceHistoryFailureCodes.FactMissing, factMissing.FailureCode);
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));
    }

    private static async Task<bool> RunMigrationsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        await runner.MigrateAsync();
        return runner.DatabaseAhead;
    }

    private static IReadOnlyList<CompiledMigrationHistoryEntry> ReadCanonicalHistory(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var plan = scope.ServiceProvider.GetRequiredService<CompiledPersistencePlan>();
        return new ModuleMigrationExecutor(dbContext, plan).ValidateBeforeConnection();
    }

    private static ServiceProvider BuildProvider(
        string applicationConnectionString,
        WorktreeDatabaseIdentity identity,
        CompiledPersistencePlan? plan = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = applicationConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan ?? TestPersistencePlan.Create(),
            identity);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static IConfiguration BuildSqlServerConfiguration(string applicationConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = applicationConnectionString
            })
            .Build();

    private static CompiledPersistencePlan BuildModulePlan(bool includeSecondMigration)
    {
        var externalModel = new ModelDefinition(
            "Asset",
            "Asset",
            ModelKind.Entity,
            [
                new FieldDefinition(
                    "Id",
                    "Id",
                    FieldKind.Scalar,
                    FieldValueType.ObjectId,
                    "string",
                    Required: true,
                    IsPrimaryKey: true,
                    IsAuditField: false),
                new FieldDefinition(
                    "Code",
                    "Code",
                    FieldKind.Scalar,
                    FieldValueType.Text,
                    "nvarchar(128)",
                    Required: true,
                    IsPrimaryKey: false,
                    IsAuditField: false)
            ],
            includeSecondMigration
                ? [new IndexDefinition("UX_Asset_Code", ["Code"], IsUnique: true)]
                : [],
            "Partner.AssetCatalog",
            ModuleSchema);
        var basePlan = TestPersistencePlan.CreateCurrentSqlServer();
        var models = basePlan.Models.Append(externalModel).ToArray();
        var planDigest = PersistenceModelDigest.Compute(models);
        var firstModelDigest = PersistenceModelDigest.Compute([externalModel with { Indexes = [] }]);
        var modelDigest = PersistenceModelDigest.Compute([externalModel]);
        var migrations = new List<CompiledMigrationStep>
        {
            new(
                "000001",
                ModuleMigrationOne,
                null,
                new string('1', 64),
                new string('0', 64),
                firstModelDigest,
                "safe-expand")
        };
        if (includeSecondMigration)
        {
            migrations.Add(new CompiledMigrationStep(
                "000002",
                ModuleMigrationTwo,
                new string('1', 64),
                new string('2', 64),
                firstModelDigest,
                modelDigest,
                "safe-expand"));
        }

        var assemblyPath = typeof(CreateAssetCatalogMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        if (includeSecondMigration)
        {
            // PE overlay 不改变程序集内容或 MVID，但模拟向 migration assembly 追加内容后
            // whole-assembly checksum 变化，验证旧 step fact 仍是有效前缀。
            assemblyBytes = [.. assemblyBytes, 0];
        }
        var stream = new ModuleMigrationStream(
            "Partner.AssetCatalog",
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-manifest.json",
            Convert.ToHexStringLower(SHA256.HashData(assemblyBytes)),
            modelDigest,
            migrations[^1].MigrationId,
            IsPlatformStream: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return new CompiledPersistencePlan(
            "test-module-candidate",
            new string('b', 64),
            new string('c', 64),
            PersistenceProvider.SqlServer,
            planDigest,
            [],
            models,
            [.. basePlan.Streams, stream]);
    }

    private static CompiledPersistencePlan BuildExampleMigrationPlan(int stage)
    {
        if (stage is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        var basePlan = TestPersistencePlan.CreateCurrentSqlServer();
        var currentModel = Assert.Single(basePlan.Models, static model =>
            string.Equals(model.OwnerModuleId, "Example", StringComparison.Ordinal)
            && string.Equals(model.Name, "ExampleRecord", StringComparison.Ordinal));
        var stageModel = stage == 2
            ? currentModel with
            {
                Fields = currentModel.Fields
                    .Where(static field => !string.Equals(field.Name, "RecordSource", StringComparison.Ordinal))
                    .ToArray()
            }
            : currentModel with
            {
                Fields = currentModel.Fields
                    .Select(static field => string.Equals(field.Name, "RecordSource", StringComparison.Ordinal)
                        ? field with { Required = false }
                        : field)
                    .ToArray()
            };
        var expectedModelDigest = stage == 2
            ? "a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c"
            : "05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf";
        Assert.Equal(expectedModelDigest, PersistenceModelDigest.Compute([stageModel]));

        var models = basePlan.Models
            .Select(model => ReferenceEquals(model, currentModel) ? stageModel : model)
            .ToArray();
        var currentStream = Assert.Single(basePlan.Streams, static stream =>
            string.Equals(stream.ModuleId, "Example", StringComparison.Ordinal));
        var migrations = currentStream.Migrations.Take(stage).ToArray();
        var stageStream = currentStream with
        {
            ModelDigest = expectedModelDigest,
            ExpectedFrontier = migrations[^1].MigrationId,
            Migrations = migrations
        };
        var streams = basePlan.Streams
            .Select(stream => ReferenceEquals(stream, currentStream) ? stageStream : stream)
            .ToArray();
        var planDigest = PersistenceModelDigest.Compute(models);
        return basePlan with
        {
            CandidateId = $"example-required-field-stage-{stage}",
            SnapshotId = planDigest,
            ModulePlanHash = planDigest,
            PlanDigest = planDigest,
            CanonicalBytes = [],
            Models = models,
            Streams = streams
        };
    }

    private static CompiledPersistencePlan BuildProviderTransitionPlan(int stage)
    {
        if (stage is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        const string owner = "Partner.ProviderTransition";
        const string schema = "np_partner_transition";
        var stageModels = Enumerable.Range(1, stage)
            .Select(BuildProviderTransitionModel)
            .ToArray();
        var finalModel = stageModels[^1];
        var basePlan = TestPersistencePlan.CreateCurrentSqlServer();
        var coreModels = basePlan.Models;
        var models = coreModels.Append(finalModel).ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var migrationIds = new[]
        {
            CreateProviderTransitionAssetMigration.MigrationId,
            AddNullableTransitionColumnMigration.MigrationId,
            AddRequiredTransitionColumnMigration.MigrationId,
            RequireTransitionValueMigration.MigrationId
        };
        var stageDigests = stageModels
            .Select(model => PersistenceModelDigest.Compute([model]))
            .ToArray();
        var migrations = Enumerable.Range(0, stage)
            .Select(index => new CompiledMigrationStep(
                $"{index + 1:D6}",
                migrationIds[index],
                index == 0 ? null : new string((char)('a' + index - 1), 64),
                new string((char)('a' + index), 64),
                index == 0 ? new string('0', 64) : stageDigests[index - 1],
                stageDigests[index],
                "safe-expand"))
            .ToArray();
        var assemblyPath = typeof(CreateProviderTransitionAssetMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-provider-transition-manifest.json",
            Convert.ToHexStringLower(SHA256.HashData(assemblyBytes)),
            stageDigests[^1],
            migrations[^1].MigrationId,
            IsPlatformStream: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return new CompiledPersistencePlan(
            $"provider-transition-stage-{stage}",
            new string('b', 64),
            new string('c', 64),
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [.. basePlan.Streams, stream]);

        ModelDefinition BuildProviderTransitionModel(int currentStage)
        {
            var fields = new List<FieldDefinition>
            {
                new(
                    "Id",
                    "Id",
                    FieldKind.Scalar,
                    FieldValueType.ObjectId,
                    "string",
                    Required: true,
                    IsPrimaryKey: true,
                    IsAuditField: false),
                new(
                    "OptionalValue",
                    "OptionalValue",
                    FieldKind.Scalar,
                    FieldValueType.Text,
                    "nvarchar(256)",
                    Required: currentStage >= 4,
                    IsPrimaryKey: false,
                    IsAuditField: false)
            };
            if (currentStage >= 2)
            {
                fields.Add(new FieldDefinition(
                    "OptionalAdded",
                    "OptionalAdded",
                    FieldKind.Scalar,
                    FieldValueType.Text,
                    "nvarchar(256)",
                    Required: false,
                    IsPrimaryKey: false,
                    IsAuditField: false));
            }
            if (currentStage >= 3)
            {
                fields.Add(new FieldDefinition(
                    "RequiredAdded",
                    "RequiredAdded",
                    FieldKind.Scalar,
                    FieldValueType.Integer,
                    "bigint",
                    Required: true,
                    IsPrimaryKey: false,
                    IsAuditField: false));
            }

            return new ModelDefinition(
                "TransitionAsset",
                "TransitionAsset",
                ModelKind.Entity,
                fields,
                [],
                owner,
                schema);
        }
    }

    private static CompiledPersistencePlan BuildFullUpPlan(int stage)
    {
        if (stage is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        const string owner = "Partner.FullUp";
        var stageModels = Enumerable.Range(1, stage)
            .Select(BuildFullUpModels)
            .ToArray();
        var finalModels = stageModels[^1];
        var basePlan = TestPersistencePlan.CreateCurrentSqlServer();
        var coreModels = basePlan.Models;
        var models = coreModels.Concat(finalModels).ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var migrationIds = new[]
        {
            FullUpMigrationOne,
            FullUpMigrationTwo,
            FullUpMigrationThree
        };
        var stageDigests = stageModels
            .Select(PersistenceModelDigest.Compute)
            .ToArray();
        var migrations = Enumerable.Range(0, stage)
            .Select(index => new CompiledMigrationStep(
                $"{index + 1:D6}",
                migrationIds[index],
                index == 0 ? null : new string((char)('a' + index - 1), 64),
                new string((char)('a' + index), 64),
                index == 0 ? new string('0', 64) : stageDigests[index - 1],
                stageDigests[index],
                "safe-expand"))
            .ToArray();
        var assemblyPath = typeof(CreateFullUpAssetMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var stream = new ModuleMigrationStream(
            owner,
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-full-up-manifest.json",
            Convert.ToHexStringLower(SHA256.HashData(assemblyBytes)),
            stageDigests[^1],
            migrations[^1].MigrationId,
            IsPlatformStream: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        return new CompiledPersistencePlan(
            $"full-up-stage-{stage}",
            new string('b', 64),
            new string('c', 64),
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [.. basePlan.Streams, stream]);

        static ModelDefinition[] BuildFullUpModels(int currentStage)
        {
            var id = new FieldDefinition(
                "Id",
                "Id",
                FieldKind.Scalar,
                FieldValueType.ObjectId,
                "string",
                Required: true,
                IsPrimaryKey: true,
                IsAuditField: false);
            var parent = new ModelDefinition(
                "Parent",
                "Parent",
                ModelKind.Entity,
                [id],
                [],
                owner,
                FullUpSchema);

            if (currentStage == 1)
            {
                var legacyAsset = new ModelDefinition(
                    "Asset",
                    "LegacyAsset",
                    ModelKind.Entity,
                    [
                        id,
                        id with
                        {
                            Name = "LegacyCode",
                            StoreName = "LegacyCode",
                            Required = false,
                            IsPrimaryKey = false
                        },
                        id with { Name = "ParentId", StoreName = "ParentId", IsPrimaryKey = false },
                        id with
                        {
                            Name = "Obsolete",
                            StoreName = "Obsolete",
                            ValueType = FieldValueType.Text,
                            StoreType = "nvarchar(256)",
                            Required = false,
                            IsPrimaryKey = false
                        }
                    ],
                    [
                        new IndexDefinition("IX_LegacyAsset_LegacyCode", ["LegacyCode"], IsUnique: false),
                        new IndexDefinition("UX_LegacyAsset_ParentId", ["ParentId"], IsUnique: true)
                    ],
                    owner,
                    FullUpSchema,
                    [new ForeignKeyDefinition(
                        "FK_LegacyAsset_Parent",
                        ["ParentId"],
                        $"{owner}/Parent",
                        ["Id"],
                        ForeignKeyDeleteBehavior.Restrict)]);
                var retired = new ModelDefinition(
                    "RetiredAsset",
                    "RetiredAsset",
                    ModelKind.Entity,
                    [id],
                    [],
                    owner,
                    FullUpSchema);
                return [parent, legacyAsset, retired];
            }

            var indexes = new List<IndexDefinition>
            {
                new("UX_Asset_ParentId", ["ParentId"], IsUnique: true)
            };
            if (currentStage >= 3)
            {
                indexes.Add(new IndexDefinition("UX_Asset_Code", ["Code"], IsUnique: true));
            }
            else
            {
                indexes.Add(new IndexDefinition("IX_Asset_Code", ["Code"], IsUnique: false));
            }

            var asset = new ModelDefinition(
                "Asset",
                "Asset",
                ModelKind.Entity,
                [
                    id,
                    id with
                    {
                        Name = "Code",
                        StoreName = "Code",
                        ValueType = FieldValueType.Text,
                        StoreType = "nvarchar(256)",
                        IsPrimaryKey = false
                    },
                    id with { Name = "ParentId", StoreName = "ParentId", IsPrimaryKey = false }
                ],
                indexes,
                owner,
                FullUpSchema,
                [new ForeignKeyDefinition(
                    "FK_Asset_Parent",
                    ["ParentId"],
                    $"{owner}/Parent",
                    ["Id"],
                    ForeignKeyDeleteBehavior.Restrict)]);
            return [parent, asset];
        }
    }

    private static CompiledPlanFixture BuildCompiledFullUpPlan(int stage)
    {
        if (stage is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "newp-compiled-full-up-plan",
            Guid.NewGuid().ToString("N"));
        var modulesRoot = Path.Combine(fixtureRoot, "modules");
        var moduleRoot = Path.Combine(modulesRoot, "Partner.FullUp");
        var modelRoot = Path.Combine(moduleRoot, "metadata", "models");
        var migrationRoot = Path.Combine(moduleRoot, "migrations");
        Directory.CreateDirectory(modelRoot);
        Directory.CreateDirectory(migrationRoot);

        var modelKey = stage == 1 ? "LegacyAsset" : "Asset";
        var modelPath = Path.Combine(modelRoot, $"{modelKey}.model.json");
        File.WriteAllText(
            modelPath,
            stage == 1
                ? """
                    {
                      "schemaVersion": "1.0",
                      "type": "model",
                      "LegacyAsset": {
                        "kind": "Entity",
                        "fields": [
                          { "name": "ALegacyCode", "storage": { "type": "string", "nullable": true } },
                          { "name": "ZObsolete", "storage": { "type": "text", "nullable": true } }
                        ],
                        "indexes": [
                          { "name": "IX_LegacyAsset_ALegacyCode", "fields": ["ALegacyCode"], "unique": false }
                        ]
                      }
                    }
                    """
                : """
                    {
                      "schemaVersion": "1.0",
                      "type": "model",
                      "Asset": {
                        "kind": "Entity",
                        "fields": [
                          { "name": "Code", "storage": { "type": "string" } }
                        ],
                        "indexes": [
                          { "name": "IX_Asset_Code", "fields": ["Code"], "unique": false }
                        ]
                      }
                    }
                    """);

        var assemblyPath = Path.Combine(migrationRoot, "Partner.FullUp.SqlServerMigrations.dll");
        File.Copy(typeof(CreateCompiledFullUpAssetMigration).Assembly.Location, assemblyPath);
        var typeMapPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json");
        var document = new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath);
        var model = new ModelMetadataProjector().Project(
            document,
            new JsonStorageTypeMapLoader().LoadFromFile(typeMapPath),
            "SqlServer") with
        {
            OwnerModuleId = "Partner.FullUp",
            SchemaName = FullUpSchema
        };
        var modelDigest = PersistenceModelDigest.Compute([model]);
        using var stageOneFixture = stage == 2 ? BuildCompiledFullUpPlan(stage: 1) : null;
        var stageOneDigest = stageOneFixture?.Plan.Streams
            .Single(static stream => stream.ModuleId == "Partner.FullUp")
            .Migrations
            .Single()
            .ToModelDigest
            ?? modelDigest;
        var manifestPath = Path.Combine(migrationRoot, "Partner.FullUp.SqlServerMigrations.json");
        var migrations = new JsonArray
        {
            new JsonObject
            {
                ["sequence"] = "000001",
                ["migrationId"] = CompiledFullUpMigrationOne,
                ["previousContentDigest"] = null,
                ["contentDigest"] = new string('c', 64),
                ["fromModelDigest"] = new string('0', 64),
                ["toModelDigest"] = stageOneDigest,
                ["compatibility"] = "safe-expand"
            }
        };
        if (stage == 2)
        {
            migrations.Add(new JsonObject
            {
                ["sequence"] = "000002",
                ["migrationId"] = CompiledFullUpMigrationTwo,
                ["previousContentDigest"] = new string('c', 64),
                ["contentDigest"] = new string('d', 64),
                ["fromModelDigest"] = stageOneDigest,
                ["toModelDigest"] = modelDigest,
                ["compatibility"] = "backup-restore-only"
            });
        }
        File.WriteAllText(
            manifestPath,
            new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["provider"] = "SqlServer",
                ["moduleId"] = "Partner.FullUp",
                ["modelDigest"] = modelDigest,
                ["bundleChecksum"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath))),
                ["migrations"] = migrations
            }.ToJsonString());

        try
        {
            var provenance = new ModulePlanProvenance(
                "installed-package",
                Path.Combine(moduleRoot, "plugin.json"),
                "/");
            var module = new CanonicalModulePlanModule(
                "Partner.FullUp",
                "1.0.0",
                "Partner Full Up",
                null,
                moduleRoot,
                null,
                null,
                null,
                null,
                "behavior-bindings",
                [],
                [],
                [],
                [],
                [],
                [],
                [modelKey],
                provenance,
                new ModulePersistenceContribution(
                    [$"metadata/models/{modelKey}.model.json"],
                    [new ModuleMigrationBundleReference(
                        "SqlServer",
                        "migrations/Partner.FullUp.SqlServerMigrations.dll",
                        "migrations/Partner.FullUp.SqlServerMigrations.json")]));
            var candidate = new ModuleCapsuleCandidate(
                module.ModuleId,
                module.Version,
                module.DisplayName,
                module.Description,
                module.PackageRoot,
                module.BackendEntry,
                module.BackendBehaviorKey,
                module.FrontendEntry,
                module.FrontendBehaviorKey,
                module.FrontendBindingMode,
                module.Dependencies,
                module.Routes,
                module.FrontendBindings,
                module.FrontendBindingReferences,
                module.PermissionCodes,
                module.PermissionReferences,
                module.ModelKeys,
                module.Provenance,
                module.Persistence);
            module = module with
            {
                Provenance = provenance.BindToCanonicalCandidate(candidate)
            };
            var canonicalPlan = new CanonicalModulePlan(
                $"compiled-full-up-{stage}",
                "test",
                new string('a', 64),
                new string('b', 64),
                [],
                [module]);
            var result = new PersistencePlanCompiler(typeMapPath).Compile(
                canonicalPlan,
                PersistenceProvider.SqlServer);
            var modulePlan = result.Plan
                ?? throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            return new CompiledPlanFixture(modulePlan, fixtureRoot);
        }
        catch
        {
            Directory.Delete(fixtureRoot, recursive: true);
            throw;
        }

    }

    private sealed class CompiledPlanFixture(
        CompiledPersistencePlan plan,
        string fixtureRoot) : IDisposable
    {
        public CompiledPersistencePlan Plan { get; } = plan;

        public void Dispose()
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static OrganizationPlanFixture BuildOrganizationPlan()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "newp-organization-plan",
            Guid.NewGuid().ToString("N"));
        var modulesRoot = Path.Combine(fixtureRoot, "modules");
        var moduleRoot = Path.Combine(modulesRoot, "Organization");
        var modelRoot = Path.Combine(moduleRoot, "metadata", "models");
        var migrationRoot = Path.Combine(moduleRoot, "migrations");
        Directory.CreateDirectory(modelRoot);
        Directory.CreateDirectory(migrationRoot);
        foreach (var modelName in new[] { "Company", "Department", "Person", "Position" })
        {
            File.Copy(
                Path.Combine(repositoryRoot, "modules", "Organization", "metadata", "models", $"{modelName}.model.json"),
                Path.Combine(modelRoot, $"{modelName}.model.json"));
        }

        var assemblyPath = Path.Combine(migrationRoot, "Organization.SqlServerMigrations.dll");
        File.Copy(typeof(Organization_000001_Foundation).Assembly.Location, assemblyPath);
        var manifestSourcePath = Path.Combine(
            repositoryRoot,
            "modules",
            "Organization",
            "migrations",
            "Organization.SqlServerMigrations.json");
        var temporaryManifestPath = Path.Combine(migrationRoot, "Organization.SqlServerMigrations.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestSourcePath))?.AsObject()
            ?? throw new InvalidOperationException("Organization migration manifest is invalid.");
        manifest["bundleChecksum"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        File.WriteAllText(temporaryManifestPath, manifest.ToJsonString());

        try
        {
            var trustedSource = TrustedBuiltInModuleSource.CreateForHost(fixtureRoot, modulesRoot);
            var provenance = ModulePlanProvenance.CreateTrustedBuiltInRoot(
                "Organization",
                moduleRoot,
                trustedSource,
                Path.Combine(moduleRoot, "plugin.json"),
                "/");
            var module = new CanonicalModulePlanModule(
                "Organization",
                "1.0.0",
                "Organization",
                null,
                moduleRoot,
                null,
                null,
                null,
                null,
                "behavior-bindings",
                ["Identity", "Authentication"],
                [],
                [],
                [],
                [],
                [],
                ["Company", "Department", "Person", "Position"],
                provenance,
                new ModulePersistenceContribution(
                    [
                        "metadata/models/Company.model.json",
                        "metadata/models/Department.model.json",
                        "metadata/models/Person.model.json",
                        "metadata/models/Position.model.json"
                    ],
                    [new ModuleMigrationBundleReference(
                        "SqlServer",
                        "migrations/Organization.SqlServerMigrations.dll",
                        "migrations/Organization.SqlServerMigrations.json")]));
            var candidate = new ModuleCapsuleCandidate(
                module.ModuleId,
                module.Version,
                module.DisplayName,
                module.Description,
                module.PackageRoot,
                module.BackendEntry,
                module.BackendBehaviorKey,
                module.FrontendEntry,
                module.FrontendBehaviorKey,
                module.FrontendBindingMode,
                module.Dependencies,
                module.Routes,
                module.FrontendBindings,
                module.FrontendBindingReferences,
                module.PermissionCodes,
                module.PermissionReferences,
                module.ModelKeys,
                module.Provenance,
                module.Persistence);
            module = module with
            {
                Provenance = provenance.BindToCanonicalCandidate(candidate)
            };
            var canonicalPlan = new CanonicalModulePlan(
                "organization-sql-smoke",
                "test",
                new string('a', 64),
                new string('b', 64),
                [],
                [module]);
            var result = new PersistencePlanCompiler(Path.Combine(
                repositoryRoot,
                "server",
                "platform-modeling",
                "metadata",
                "storage-types.json")).Compile(
                canonicalPlan,
                PersistenceProvider.SqlServer);
            var organizationPlan = result.Plan
                ?? throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic => diagnostic.Message)));

            var organizationModels = organizationPlan.Models;
            var organizationDigest = PersistenceModelDigest.Compute(organizationModels);
            var foundationPlan = TestPersistencePlan.Create();
            var coreModels = foundationPlan.Models;
            var models = coreModels.Concat(organizationModels).ToArray();
            var streams = foundationPlan.Streams
                .Append(Assert.Single(organizationPlan.Streams))
                .ToArray();
            return new OrganizationPlanFixture(
                new CompiledPersistencePlan(
                    "organization-migration-test",
                    organizationDigest,
                    organizationDigest,
                    PersistenceProvider.SqlServer,
                    organizationDigest,
                    [],
                    models,
                    streams),
                fixtureRoot);
        }
        catch
        {
            Directory.Delete(fixtureRoot, recursive: true);
            throw;
        }
    }

    private sealed class OrganizationPlanFixture(
        CompiledPersistencePlan plan,
        string fixtureRoot) : IDisposable
    {
        public CompiledPersistencePlan Plan { get; } = plan;

        public void Dispose()
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static async Task<string> CreateCatalogAsync(
        string adminConnectionString,
        WorktreeDatabaseIdentity identity,
        string purpose)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var catalog = $"{identity.CatalogName}_b2_{suffix}_{purpose}";
        ValidateCatalogName(identity, catalog);

        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}];";
        await command.ExecuteNonQueryAsync();
        return catalog;
    }

    private static async Task DropCreatedCatalogAsync(
        string adminConnectionString,
        WorktreeDatabaseIdentity identity,
        string catalog)
    {
        ValidateCatalogName(identity, catalog);
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] = @catalog;";
            countCommand.Parameters.AddWithValue("@catalog", catalog);
            var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            if (count is 0)
            {
                return;
            }

            Assert.Equal(1, count);
        }

        await using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = $"""
            ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{catalog}];
            """;
        await dropCommand.ExecuteNonQueryAsync();
    }

    private static void ValidateCatalogName(WorktreeDatabaseIdentity identity, string catalog)
    {
        var pattern = $"^{Regex.Escape(identity.CatalogName)}_b2_[a-f0-9]{{8}}_[a-z]+$";
        if (!Regex.IsMatch(catalog, pattern, RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("拒绝操作不符合本轮 worktree smoke 命名边界的数据库。");
        }
    }

    private static string ResolveAdminConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable);
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder(
                "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;")
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = "master";
        builder.Pooling = false;
        return builder.ConnectionString;
    }

    private static string BuildCatalogConnectionString(string adminConnectionString, string catalog)
    {
        var builder = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<int> CountBusinessDefaultConstraintsAsync(string connectionString) =>
        Convert.ToInt32(await ExecuteScalarAsync(connectionString, """
            SELECT COUNT(*)
            FROM [sys].[default_constraints] AS [d]
            INNER JOIN [sys].[tables] AS [t] ON [t].[object_id] = [d].[parent_object_id]
            WHERE [t].[is_ms_shipped] = 0
              AND [t].[name] NOT LIKE N'__%';
            """));

    private static async Task<int> CountNullableAuditColumnsAsync(string connectionString) =>
        Convert.ToInt32(await ExecuteScalarAsync(connectionString, """
            SELECT COUNT(*)
            FROM [sys].[columns] AS [c]
            INNER JOIN [sys].[tables] AS [t] ON [t].[object_id] = [c].[object_id]
            WHERE [t].[is_ms_shipped] = 0
              AND [t].[name] NOT LIKE N'__%'
              AND [c].[name] IN (N'CreatedBy', N'CreatedAt', N'UpdatedBy', N'UpdatedAt')
              AND [c].[is_nullable] = 1;
            """));

    private static async Task<int> CountMigrationFactsAsync(string connectionString) =>
        Convert.ToInt32(await ExecuteScalarAsync(
            connectionString,
            "SELECT COUNT(*) FROM [dbo].[__NewPMigrationHistory];"));

    private static async Task<bool> ColumnExistsAsync(
        string connectionString,
        string schema,
        string table,
        string column)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [sys].[columns] AS [columns]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [columns].[object_id]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [tables].[name] = @table
              AND [columns].[name] = @column;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnIsNullableAsync(
        string connectionString,
        string schema,
        string table,
        string column)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [columns].[is_nullable]
            FROM [sys].[columns] AS [columns]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [columns].[object_id]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [tables].[name] = @table
              AND [columns].[name] = @column;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ReadCharacterMaximumLengthAsync(
        string connectionString,
        string schema,
        string table,
        string column)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [CHARACTER_MAXIMUM_LENGTH]
            FROM [INFORMATION_SCHEMA].[COLUMNS]
            WHERE [TABLE_SCHEMA] = @schema
              AND [TABLE_NAME] = @table
              AND [COLUMN_NAME] = @column;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> DefaultConstraintExistsAsync(
        string connectionString,
        string schema,
        string table,
        string column)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [sys].[default_constraints] AS [defaults]
            INNER JOIN [sys].[columns] AS [columns]
                ON [columns].[object_id] = [defaults].[parent_object_id]
               AND [columns].[column_id] = [defaults].[parent_column_id]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [columns].[object_id]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [tables].[name] = @table
              AND [columns].[name] = @column;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) != 0;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [sys].[tables] WHERE [name] = @tableName;";
        command.Parameters.AddWithValue("@tableName", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> SchemaObjectExistsAsync(
        string connectionString,
        string schema,
        string objectName,
        string objectType)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [sys].[objects] AS [objects]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [objects].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [objects].[name] = @objectName
              AND [objects].[type] = @objectType;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@objectName", objectName);
        command.Parameters.AddWithValue("@objectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexExistsAsync(
        string connectionString,
        string schema,
        string table,
        string index)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [sys].[indexes] AS [indexes]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [indexes].[object_id]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [tables].[name] = @table
              AND [indexes].[name] = @index;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexIsUniqueAsync(
        string connectionString,
        string schema,
        string table,
        string index)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [indexes].[is_unique]
            FROM [sys].[indexes] AS [indexes]
            INNER JOIN [sys].[tables] AS [tables] ON [tables].[object_id] = [indexes].[object_id]
            INNER JOIN [sys].[schemas] AS [schemas] ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE [schemas].[name] = @schema
              AND [tables].[name] = @table
              AND [indexes].[name] = @index;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<string[]> ReadAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];";
        await using var reader = await command.ExecuteReaderAsync();
        var migrations = new List<string>();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations.ToArray();
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))
            .ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

[CollectionDefinition(SqlServerPersistenceSmokeCollection.Name, DisableParallelization = true)]
public sealed class SqlServerPersistenceSmokeCollection
{
    public const string Name = "SqlServerPersistenceSmoke";
}
