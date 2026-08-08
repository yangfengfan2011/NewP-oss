using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.Ownership;
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
    private const string LatestMigration = "20260808054041_AlignOrganizationSchemaDrift";

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

    private static async Task<bool> RunMigrationsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        await runner.MigrateAsync();
        return runner.DatabaseAhead;
    }

    private static ServiceProvider BuildProvider(
        string applicationConnectionString,
        WorktreeDatabaseIdentity identity)
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
            PlatformPersistenceModelCatalog.GetDefinitions(),
            identity);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
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
