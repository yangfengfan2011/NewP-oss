using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

[Collection(SqlServerPersistenceSmokeCollection.Name)]
public sealed class SqlServerPersistenceSmokeTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_SQLSERVER_SMOKE";
    private const string AdminConnectionEnvironmentVariable = "NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION";
    private const string PreviousMigration = "20260428145214_AlignModelMetadataEntityKeys";
    private const string PersonPermanentBindingMigration = "20260810102141_Organization_000001_PermanentUserBinding";
    private const string PersonLifecycleRevisionMigration = "20260810111523_Organization_000002_PersonLifecycleRevision";
    private const string LatestMigration = "20260808054041_AlignOrganizationSchemaDrift";
    private const string ModuleMigrationOne = "20260809001000_CreateAssetCatalog";
    private const string ModuleMigrationTwo = "20260809002000_AddUniqueAssetCode";
    private const string ModuleSchema = "np_partner_assetcatalog";

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_EmptyCatalogMigratesForwardAndWarmStartIsNoOp() =>
        RunCatalogScenarioAsync("forward", VerifyForwardMigrationAndWarmNoOpAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_NonEmptyUnownedCatalogIsRejected() =>
        RunCatalogScenarioAsync("unowned", VerifyNonEmptyUnownedCatalogIsRejectedAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_CatalogFingerprintMismatchIsRejected() =>
        RunCatalogScenarioAsync("mismatch", VerifyCatalogFingerprintMismatchIsRejectedAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerBootstrap_MigrationFailureKeepsReadableSafePrefix() =>
        RunCatalogScenarioAsync("failure", VerifyMigrationFailureKeepsReadableSafePrefixAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerModuleMigrations_ApplyNoOpFailureRetryAndOldPlanAhead() =>
        RunCatalogScenarioAsync("module", VerifyModuleMigrationLifecycleAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerLegacyFrontier_AppliesOrganizationOwnedPermanentBindingAndRevision() =>
        RunCatalogScenarioAsync("revision", VerifyPersonRevisionUpgradeAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerLegacyFrontier_NonEmptyPersonBlocksRevisionWithoutDefaultAndRetries() =>
        RunCatalogScenarioAsync("revisionguard", VerifyPersonRevisionPreconditionAsync);

    [Fact]
    [Trait("Category", "SqlServerSmoke")]
    public Task SqlServerProviderTransitions_EnforceDataPreconditionsWithoutDefaultsOrBackfill() =>
        RunCatalogScenarioAsync("target", VerifyProviderTransitionDataBoundariesAsync);

    private static async Task RunCatalogScenarioAsync(
        string purpose,
        Func<string, string, WorktreeDatabaseIdentity, Task> scenario)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var identity = WorktreeDatabaseIdentity.Resolve(repositoryRoot);
        var adminConnectionString = ResolveAdminConnectionString();
        string? catalog = null;

        try
        {
            catalog = await CreateCatalogAsync(adminConnectionString, identity, purpose);
            await scenario(adminConnectionString, catalog, identity);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (catalog is not null)
            {
                await DropCreatedCatalogAsync(adminConnectionString, identity, catalog);
            }
        }
    }

    private static async Task VerifyForwardMigrationAndWarmNoOpAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using var provider = BuildProvider(applicationConnectionString, identity);

        await using (var scope = provider.CreateAsyncScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationGuard>();
            await guard.VerifyAsync();
            var context = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await ExecuteNonQueryAsync(applicationConnectionString, """
            INSERT INTO [Company]
                ([EID], [Code], [Name], [SortOrder], [IsEnabled], [CreatedAt], [UpdatedAt])
            VALUES
                (N'company-smoke', N'COMPANY-SMOKE', N'迁移前公司', N'1', 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [Person]
                ([EID], [PersonNo], [Name], [ENAME], [IsEnabled], [CreatedAt], [UpdatedAt])
            VALUES
                (N'person-smoke', N'P-SMOKE', N'迁移前人员', N'', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """);

        Assert.False(await RunMigrationsAsync(provider));

        await using (var connection = new SqlConnection(applicationConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT [ECODE], [ENAME]
                FROM [Company]
                WHERE [EID] = N'company-smoke';

                SELECT [ENAME]
                FROM [Person]
                WHERE [EID] = N'person-smoke';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("COMPANY-SMOKE", reader.GetString(0));
            Assert.Equal("迁移前公司", reader.GetString(1));

            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal("迁移前人员", reader.GetString(0));
        }

        var devSyncOptions = new DevelopmentSchemaSyncOptions(
            FindRepositoryRoot(),
            applicationConnectionString,
            "SqlServer",
            ApplyChanges: false,
            GenerateSqlPreview: false,
            AllowConfirmOperations: true);
        var devSyncResult = await DevelopmentSchemaSyncCoordinator.Create(devSyncOptions).PrepareAsync();
        Assert.True(devSyncResult.StartupAllowed, devSyncResult.BuildMessage());
        var reportedOperations = devSyncResult.Plan?.Operations ?? [];
        var expectedOperations = new[]
        {
            "AddColumn:Person:Revision",
            "AddColumn:Person:UserBindingReason",
            "AddColumn:Person:UserBoundAt",
            "AddColumn:Person:UserBoundBy",
            "AddColumn:Person:UserEID",
            "CreateTable:AuthenticationSession:",
            "CreateTable:IdentitySecurityEvent:",
            "CreateTable:LocalCredential:",
            "CreateTable:User:"
        }
            .Concat(new[]
            {
                "Company",
                "ConfigItem",
                "Department",
                "Permission",
                "Person",
                "Position",
                "Role",
                "RolePermission",
                "UserRole",
                "ValueListItem"
            }.SelectMany(static table => new[]
            {
                $"AlterNullability:{table}:CreatedBy",
                $"AlterNullability:{table}:UpdatedBy"
            }))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedOperations,
            reportedOperations
                .Select(static operation => $"{operation.Kind}:{operation.TableName}:{operation.ColumnName}")
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(
            reportedOperations.Where(static operation => operation.Kind == "AlterNullability"),
            static operation => Assert.Equal("Confirm", operation.RiskLevel));
        Assert.All(
            reportedOperations.Where(static operation => operation.Kind is "AddColumn" or "CreateTable"),
            static operation => Assert.Equal("Safe", operation.RiskLevel));
        Assert.Contains("dbo.__NewPDatabaseOwnership", devSyncResult.Plan?.UnmanagedTables ?? []);

        var historyBeforeWarmStart = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(LatestMigration, historyBeforeWarmStart);

        Assert.False(await RunMigrationsAsync(provider));

        var historyAfterWarmStart = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(historyBeforeWarmStart, historyAfterWarmStart);

        await ExecuteNonQueryAsync(applicationConnectionString, """
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'20990101000000_FutureMigration', N'99.0.0');
            """);
        Assert.True(await RunMigrationsAsync(provider));
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "SELECT TOP (0) [ECODE], [ENAME] FROM [Company]; SELECT TOP (0) [ENAME] FROM [Person];");
        Assert.Equal(
            "20990101000000_FutureMigration",
            (await ReadAppliedMigrationsAsync(applicationConnectionString))[^1]);

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            applicationConnectionString,
            "UPDATE [dbo].[__NewPDatabaseOwnership] SET [Version] = 2 WHERE [MarkerId] = 1;"));
        Assert.Equal(51000, exception.Number);
    }

    private static async Task VerifyPersonRevisionUpgradeAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using (var legacyProvider = BuildProvider(
                         applicationConnectionString,
                         identity,
                         BuildLegacyFrontierPlan(PlatformCoreMigrationFrontiers.SqlServerLegacyBaseline)))
        {
            Assert.False(await RunMigrationsAsync(legacyProvider));
        }

        await ExecuteNonQueryAsync(applicationConnectionString, """
            INSERT INTO [dbo].[PersonUserLink]
                ([EID], [PersonId], [UserId], [LinkedAt], [LinkedBy], [CreatedBy], [CreatedAt], [UpdatedBy], [UpdatedAt])
            VALUES
                (N'legacy-link', N'legacy-person', N'legacy-user', SYSUTCDATETIME(), N'legacy-actor', N'legacy-actor', SYSUTCDATETIME(), N'legacy-actor', SYSUTCDATETIME());
            """);

        using var organizationFixture = BuildOrganizationPlan();
        await using var provider = BuildProvider(applicationConnectionString, identity, organizationFixture.Plan);

        Assert.False(await RunMigrationsAsync(provider));

        var history = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(LatestMigration, history);
        Assert.Contains(PersonPermanentBindingMigration, history);
        Assert.Contains(PersonLifecycleRevisionMigration, history);
        Assert.False(await TableExistsAsync(applicationConnectionString, "PersonUserLink"));
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "SELECT TOP (0) [UserEID], [UserBoundAt], [UserBoundBy], [UserBindingReason], [Revision] FROM [dbo].[Person];");
        Assert.True(await IndexExistsAsync(applicationConnectionString, "dbo", "Person", "UX_Person_UserEID"));
        Assert.False(await DefaultConstraintExistsAsync(
            applicationConnectionString,
            "dbo",
            "Person",
            "Revision"));
    }

    private static async Task VerifyPersonRevisionPreconditionAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using (var legacyProvider = BuildProvider(applicationConnectionString, identity))
        {
            await using (var scope = legacyProvider.CreateAsyncScope())
            {
                var guard = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationGuard>();
                await guard.VerifyAsync();
                var context = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            await ExecuteNonQueryAsync(applicationConnectionString, """
                INSERT INTO [Person]
                    ([EID], [PersonNo], [Name], [ENAME], [IsEnabled], [CreatedAt], [UpdatedAt])
                VALUES
                    (N'person-revision-guard', N'P-REVISION-GUARD', N'迁移前人员', N'', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                """);
            Assert.False(await RunMigrationsAsync(legacyProvider));
        }

        using var organizationFixture = BuildOrganizationPlan();
        await using var provider = BuildProvider(applicationConnectionString, identity, organizationFixture.Plan);
        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() => RunMigrationsAsync(provider));
        Assert.True(
            string.Equals(
                PersistenceMigrationFailureCodes.TableNotEmpty,
                exception.FailureCode,
                StringComparison.Ordinal),
            exception.ToString());

        var historyAfterFailure = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(PersonPermanentBindingMigration, historyAfterFailure);
        Assert.DoesNotContain(PersonLifecycleRevisionMigration, historyAfterFailure);
        Assert.False(await ColumnExistsAsync(applicationConnectionString, "dbo", "Person", "Revision"));

        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "DELETE FROM [dbo].[Person] WHERE [EID] = N'person-revision-guard';");
        Assert.False(await RunMigrationsAsync(provider));

        Assert.Contains(PersonLifecycleRevisionMigration, await ReadAppliedMigrationsAsync(applicationConnectionString));
        Assert.True(await ColumnExistsAsync(applicationConnectionString, "dbo", "Person", "Revision"));
        Assert.False(await DefaultConstraintExistsAsync(
            applicationConnectionString,
            "dbo",
            "Person",
            "Revision"));
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

        Assert.Equal(DatabaseOwnershipFailureCodes.DatabaseUnowned, exception.FailureCode);
        Assert.False(await TableExistsAsync(applicationConnectionString, "__NewPDatabaseOwnership"));
        Assert.False(await TableExistsAsync(applicationConnectionString, "__EFMigrationsHistory"));
        Assert.True(await TableExistsAsync(applicationConnectionString, "ForeignTable"));
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

    private static async Task VerifyMigrationFailureKeepsReadableSafePrefixAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        await using var provider = BuildProvider(applicationConnectionString, identity);

        await using (var scope = provider.CreateAsyncScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationGuard>();
            await guard.VerifyAsync();
            var context = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "ALTER TABLE [Company] ADD [ECODE] nvarchar(256) NULL;");

        await Assert.ThrowsAnyAsync<Exception>(() => RunMigrationsAsync(provider));

        var history = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Equal(PreviousMigration, history[^1]);
        Assert.DoesNotContain(LatestMigration, history);
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            "SELECT TOP (0) [Code], [Name] FROM [Company]; SELECT TOP (0) [Name] FROM [Person];");
    }

    private static async Task VerifyModuleMigrationLifecycleAsync(
        string adminConnectionString,
        string catalog,
        WorktreeDatabaseIdentity identity)
    {
        var applicationConnectionString = BuildCatalogConnectionString(adminConnectionString, catalog);
        var firstPlan = BuildModulePlan(includeSecondMigration: false);
        var secondPlan = BuildModulePlan(includeSecondMigration: true);

        using var firstProvider = BuildProvider(applicationConnectionString, identity, firstPlan);
        Assert.False(await RunMigrationsAsync(firstProvider));
        var firstHistory = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(ModuleMigrationOne, firstHistory);
        Assert.DoesNotContain(ModuleMigrationTwo, firstHistory);
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
        Assert.False(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            $"SELECT TOP (0) [Id], [Code] FROM [{ModuleSchema}].[Asset];");

        // Candidate 失败时，仍处于旧 frontier 的 Active 可继续启动并读写。
        Assert.False(await RunMigrationsAsync(firstProvider));
        await ExecuteNonQueryAsync(applicationConnectionString, $"""
            DELETE FROM [{ModuleSchema}].[Asset] WHERE [Id] = N'asset-2';
            """);

        Assert.False(await RunMigrationsAsync(secondProvider));
        var completedHistory = await ReadAppliedMigrationsAsync(applicationConnectionString);
        Assert.Contains(ModuleMigrationTwo, completedHistory);
        Assert.True(await IndexExistsAsync(
            applicationConnectionString,
            ModuleSchema,
            "Asset",
            "UX_Asset_Code"));

        Assert.False(await RunMigrationsAsync(secondProvider));
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));

        // 新 frontier 生效后，旧 Active 只标记 database-ahead，绝不尝试 Down。
        Assert.True(await RunMigrationsAsync(firstProvider));
        await ExecuteNonQueryAsync(
            applicationConnectionString,
            $"SELECT TOP (0) [Id], [Code] FROM [{ModuleSchema}].[Asset];");
        Assert.Equal(completedHistory, await ReadAppliedMigrationsAsync(applicationConnectionString));
    }

    private static async Task<bool> RunMigrationsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        await runner.MigrateAsync();
        return runner.DatabaseAhead;
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
                ["Persistence:AutoMigrate"] = "true",
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
        var models = TestPersistencePlan.Create().Models.Append(externalModel).ToArray();
        var modelDigest = PersistenceModelDigest.Compute(models);
        var migrations = new List<CompiledMigrationStep>
        {
            new(
                "000001",
                ModuleMigrationOne,
                null,
                new string('1', 64),
                new string('0', 64),
                modelDigest,
                "safe-expand")
        };
        if (includeSecondMigration)
        {
            migrations.Add(new CompiledMigrationStep(
                "000002",
                ModuleMigrationTwo,
                new string('1', 64),
                new string('2', 64),
                modelDigest,
                modelDigest,
                "safe-expand"));
        }

        var assemblyPath = typeof(CreateAssetCatalogMigration).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var stream = new ModuleMigrationStream(
            "Partner.AssetCatalog",
            PersistenceProvider.SqlServer,
            assemblyPath,
            "test-manifest.json",
            Convert.ToHexStringLower(SHA256.HashData(assemblyBytes)),
            modelDigest,
            migrations[^1].MigrationId,
            IsLegacyBaseline: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        var legacyStream = new ModuleMigrationStream(
            "platform.core",
            PersistenceProvider.SqlServer,
            EfCoreProviderOptions.GetDefaultMigrationsAssembly(PersistenceProvider.SqlServer),
            "platform.core:legacy-baseline",
            modelDigest,
            modelDigest,
            LatestMigration,
            IsLegacyBaseline: true,
            []);
        return new CompiledPersistencePlan(
            "test-module-candidate",
            new string('b', 64),
            new string('c', 64),
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [legacyStream, stream]);
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
        var coreModels = TestPersistencePlan.Create().Models;
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
            IsLegacyBaseline: false,
            migrations)
        {
            AssemblyImage = new VerifiedArtifactImage(assemblyBytes)
        };
        var legacyStream = new ModuleMigrationStream(
            "platform.core",
            PersistenceProvider.SqlServer,
            EfCoreProviderOptions.GetDefaultMigrationsAssembly(PersistenceProvider.SqlServer),
            "platform.core:legacy-baseline",
            modelDigest,
            modelDigest,
            LatestMigration,
            IsLegacyBaseline: true,
            []);
        return new CompiledPersistencePlan(
            $"provider-transition-stage-{stage}",
            new string('b', 64),
            new string('c', 64),
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [legacyStream, stream]);

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

    private static CompiledPersistencePlan BuildLegacyFrontierPlan(string frontier)
    {
        var models = TestPersistencePlan.Create().Models;
        var modelDigest = PersistenceModelDigest.Compute(models);
        var stream = new ModuleMigrationStream(
            "platform.core",
            PersistenceProvider.SqlServer,
            EfCoreProviderOptions.GetDefaultMigrationsAssembly(PersistenceProvider.SqlServer),
            "platform.core:legacy-baseline",
            modelDigest,
            modelDigest,
            frontier,
            IsLegacyBaseline: true,
            []);
        return new CompiledPersistencePlan(
            "legacy-frontier-test",
            modelDigest,
            modelDigest,
            PersistenceProvider.SqlServer,
            modelDigest,
            [],
            models,
            [stream]);
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
        File.Copy(typeof(OrganizationPermanentUserBinding).Assembly.Location, assemblyPath);
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
            var coreModels = TestPersistencePlan.Create().Models
                .Where(static model => model.Name is not ("Company" or "Department" or "Person" or "Position"))
                .ToArray();
            var models = coreModels.Concat(organizationModels).ToArray();
            var coreDigest = PersistenceModelDigest.Compute(coreModels);
            var streams = new[]
            {
                new ModuleMigrationStream(
                    "platform.core",
                    PersistenceProvider.SqlServer,
                    EfCoreProviderOptions.GetDefaultMigrationsAssembly(PersistenceProvider.SqlServer),
                    "platform.core:legacy-baseline",
                    coreDigest,
                    coreDigest,
                    PlatformCoreMigrationFrontiers.SqlServerLegacyBaseline,
                    IsLegacyBaseline: true,
                    []),
                Assert.Single(organizationPlan.Streams)
            };
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
