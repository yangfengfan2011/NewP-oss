using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        Assert.Equal(11, reportedOperations.Count);
        Assert.All(reportedOperations, static operation =>
        {
            Assert.Equal("AlterNullability", operation.Kind);
            Assert.Equal("UpdatedAt", operation.ColumnName);
            Assert.Equal("Confirm", operation.RiskLevel);
        });
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

        await using var provider = BuildProvider(applicationConnectionString, identity, BuildOrganizationPlan());

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
                    "nvarchar(64)",
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

        var stream = new ModuleMigrationStream(
            "Partner.AssetCatalog",
            PersistenceProvider.SqlServer,
            typeof(CreateAssetCatalogMigration).Assembly.Location,
            "test-manifest.json",
            new string('a', 64),
            modelDigest,
            migrations[^1].MigrationId,
            IsLegacyBaseline: false,
            migrations);
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

    private static CompiledPersistencePlan BuildOrganizationPlan()
    {
        var organizationModels = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static model => model with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        var organizationDigest = PersistenceModelDigest.Compute(organizationModels);
        var coreModels = TestPersistencePlan.Create().Models
            .Where(static model => model.Name is not ("Company" or "Department" or "Person" or "Position"))
            .ToArray();
        var models = coreModels.Concat(organizationModels).ToArray();
        var coreDigest = PersistenceModelDigest.Compute(coreModels);
        var migrations = new[]
        {
            new CompiledMigrationStep(
                "000001",
                PersonPermanentBindingMigration,
                null,
                new string('1', 64),
                new string('0', 64),
                new string('2', 64),
                "backup-restore-only"),
            new CompiledMigrationStep(
                "000002",
                PersonLifecycleRevisionMigration,
                new string('1', 64),
                new string('3', 64),
                new string('2', 64),
                organizationDigest,
                "safe-expand")
        };
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
            new ModuleMigrationStream(
                "Organization",
                PersistenceProvider.SqlServer,
                typeof(OrganizationPermanentUserBinding).Assembly.Location,
                "Organization.SqlServerMigrations.json",
                new string('4', 64),
                organizationDigest,
                PersonLifecycleRevisionMigration,
                IsLegacyBaseline: false,
                migrations)
        };
        return new CompiledPersistencePlan(
            "organization-migration-test",
            organizationDigest,
            organizationDigest,
            PersistenceProvider.SqlServer,
            organizationDigest,
            [],
            models,
            streams);
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

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class CreateAssetCatalogMigration : Migration
{
    public const string MigrationId = ModuleMigrationIds.CreateAssetCatalog;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(SqlServerModuleMigrationNames.Schema);
        migrationBuilder.CreateTable(
            name: "Asset",
            schema: SqlServerModuleMigrationNames.Schema,
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", nullable: false),
                Code = table.Column<string>(type: "nvarchar(128)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Asset", asset => asset.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddUniqueAssetCodeMigration : Migration
{
    public const string MigrationId = ModuleMigrationIds.AddUniqueAssetCode;

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "UX_Asset_Code",
            schema: SqlServerModuleMigrationNames.Schema,
            table: "Asset",
            column: "Code",
            unique: true);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

internal static class ModuleMigrationIds
{
    public const string CreateAssetCatalog = "20260809001000_CreateAssetCatalog";
    public const string AddUniqueAssetCode = "20260809002000_AddUniqueAssetCode";
}

internal static class SqlServerModuleMigrationNames
{
    public const string Schema = "np_partner_assetcatalog";
}

[CollectionDefinition(SqlServerPersistenceSmokeCollection.Name, DisableParallelization = true)]
public sealed class SqlServerPersistenceSmokeCollection
{
    public const string Name = "SqlServerPersistenceSmoke";
}
