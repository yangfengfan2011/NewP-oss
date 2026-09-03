using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Testing;
using ICP.Platform.Tools.MigrationManifest;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Tools.MigrationManifest.Tests;

public sealed class MigrationManifestVerticalReplayTests : IDisposable
{
    private const string DatabaseEvidenceEnvironmentVariable = "NEWP_RUN_MIGRATION_MANIFEST_DB_READONLY";
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"newp-migration-manifest-vertical-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;

    public MigrationManifestVerticalReplayTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void OrganizationTail_InIsolatedRepository_FinalizesIdempotentlyAndPassesFormalCheck()
    {
        var repositoryRoot = FindRepositoryRoot();
        var protectedPaths = GetProtectedAssetPaths(repositoryRoot);
        var protectedSnapshot = SnapshotFiles(protectedPaths);
        InitializeIsolatedOrganizationRepository(repositoryRoot);
        var manifestPath = Path.Combine(
            _fixtureRoot,
            "modules",
            "Organization",
            "migrations",
            "Organization.SqlServerMigrations.json");
        var manifest = MigrationManifestIntegrity.Parse(File.ReadAllBytes(manifestPath));
        var tail = manifest.Migrations.OrderBy(static migration => migration.Sequence, StringComparer.Ordinal).Last();
        var prefix = manifest with { Migrations = manifest.Migrations.Take(manifest.Migrations.Count - 1).ToArray() };
        File.WriteAllBytes(manifestPath, MigrationManifestIntegrity.SerializeCanonical(prefix));
        var application = new MigrationManifestApplication(_fixtureRoot);

        var first = application.Finalize("Organization", "SqlServer", tail.MigrationId, tail.Compatibility);
        var firstBytes = File.ReadAllBytes(manifestPath);
        var firstHash = Convert.ToHexStringLower(SHA256.HashData(firstBytes));
        var second = application.Finalize("Organization", "SqlServer", tail.MigrationId, compatibility: null);
        var secondBytes = File.ReadAllBytes(manifestPath);
        var secondHash = Convert.ToHexStringLower(SHA256.HashData(secondBytes));
        File.Copy(
            manifestPath,
            Path.Combine(
                _fixtureRoot,
                "apps",
                "host-api",
                "bin",
                "Release",
                "net10.0",
                "modules",
                "Organization",
                "migrations",
                "Organization.SqlServerMigrations.json"),
            overwrite: true);
        InitializeIsolatedGitRepository();
        var checkAssets = GetProtectedAssetPaths(_fixtureRoot);
        var checkAssetSnapshot = SnapshotFiles(checkAssets);
        var gitSnapshot = CaptureGitSnapshot();
        Assert.Contains("1 M.", gitSnapshot.Status, StringComparison.Ordinal);
        Assert.Contains("staged-sentinel.txt", gitSnapshot.Status, StringComparison.Ordinal);
        Assert.Contains("1 .M", gitSnapshot.Status, StringComparison.Ordinal);
        Assert.Contains("unstaged-sentinel.txt", gitSnapshot.Status, StringComparison.Ordinal);

        var check = application.CheckAll(noBuild: true);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(firstHash, secondHash);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(1, check.CheckedBundles);
        Assert.Contains("SqlServer: 1 bundle checked", check.ProviderResults);
        Assert.Equal(checkAssetSnapshot, SnapshotFiles(checkAssets));
        Assert.Equal(gitSnapshot, CaptureGitSnapshot());
        Assert.Equal(protectedSnapshot, SnapshotFiles(protectedPaths));
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            module = "Organization",
            provider = "SqlServer",
            migration = tail.MigrationId,
            compatibility = tail.Compatibility,
            firstFinalizeChanged = first.Changed,
            secondFinalizeChanged = second.Changed,
            manifestSha256 = secondHash,
            checkedBundles = check.CheckedBundles,
            providerResults = check.ProviderResults,
            gitHead = gitSnapshot.Head,
            gitIndexTree = gitSnapshot.IndexTree,
            gitStatus = gitSnapshot.Status,
            gitTrackedContentSha256 = gitSnapshot.TrackedContentSha256,
            checkAssetsSha256 = checkAssetSnapshot,
            protectedAssetsUnchanged = true
        }));
    }

    [EnvironmentGatedFact(
        DatabaseEvidenceEnvironmentVariable,
        "SQL Server 每库日志前沿与 migration history 内容的只读前后快照；配置来源与 Acceptance admin connection 一致")]
    [Trait("Category", "SqlServerReadOnlyEvidence")]
    public async Task CheckAll_WithSqlServerReadOnlyObserver_PreservesDatabaseState()
    {
        var repositoryRoot = FindRepositoryRoot();
        InitializeIsolatedOrganizationRepository(repositoryRoot);
        var observerConfiguration = ResolveSqlServerObserver(repositoryRoot);
        await using var observer = await SqlServerDatabaseObserver.OpenAsync(observerConfiguration.ConnectionString);
        var before = await observer.CaptureAsync();

        var check = new MigrationManifestApplication(_fixtureRoot).CheckAll(noBuild: true);

        var after = await observer.CaptureAsync();
        Assert.Equal(before, after);
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            databaseProbe = "sys.dm_db_log_stats plus __NewPMigrationHistory content",
            connectionSource = observerConfiguration.Source,
            databaseCount = after.DatabaseCount,
            logFrontierSha256 = after.LogFrontierSha256,
            historyTableCount = after.HistoryTableCount,
            historyRowCount = after.HistoryRowCount,
            historyContentSha256 = after.HistoryContentSha256,
            checkedBundles = check.CheckedBundles,
            unchanged = true
        }));
    }

    [EnvironmentGatedFact(
        DatabaseEvidenceEnvironmentVariable,
        "SQL Server 只读快照必须感知夹具提交的业务与 migration history 写入")]
    [Trait("Category", "SqlServerReadOnlyEvidence")]
    public async Task DatabaseSnapshot_WhenFixtureCommitsBusinessAndHistoryWrites_Changes()
    {
        var repositoryRoot = FindRepositoryRoot();
        InitializeIsolatedOrganizationRepository(repositoryRoot);
        var observerConfiguration = ResolveSqlServerObserver(repositoryRoot);
        var catalog = $"NewPMigrationManifestEvidence_{Guid.NewGuid():N}";
        await CreateEvidenceCatalogAsync(observerConfiguration.ConnectionString, catalog);
        try
        {
            await using var observer = await SqlServerDatabaseObserver.OpenAsync(observerConfiguration.ConnectionString);
            var beforeBusinessWrite = await observer.CaptureAsync();
            _ = new MigrationManifestApplication(_fixtureRoot).CheckAll(noBuild: true);
            await InjectBusinessWriteAsync(observerConfiguration.ConnectionString, catalog);
            var afterBusinessWrite = await observer.CaptureAsync();
            Assert.NotEqual(beforeBusinessWrite, afterBusinessWrite);
            Assert.NotEqual(beforeBusinessWrite.LogFrontierSha256, afterBusinessWrite.LogFrontierSha256);
            Assert.Equal(beforeBusinessWrite.HistoryContentSha256, afterBusinessWrite.HistoryContentSha256);

            _ = new MigrationManifestApplication(_fixtureRoot).CheckAll(noBuild: true);
            await InjectSchemaWriteAsync(observerConfiguration.ConnectionString, catalog);
            var afterSchemaWrite = await observer.CaptureAsync();
            Assert.NotEqual(afterBusinessWrite, afterSchemaWrite);
            Assert.NotEqual(afterBusinessWrite.LogFrontierSha256, afterSchemaWrite.LogFrontierSha256);
            Assert.Equal(afterBusinessWrite.HistoryContentSha256, afterSchemaWrite.HistoryContentSha256);

            _ = new MigrationManifestApplication(_fixtureRoot).CheckAll(noBuild: true);
            await InjectHistoryWriteAsync(observerConfiguration.ConnectionString, catalog);
            var afterHistoryWrite = await observer.CaptureAsync();
            Assert.NotEqual(afterSchemaWrite, afterHistoryWrite);
            Assert.NotEqual(afterSchemaWrite.LogFrontierSha256, afterHistoryWrite.LogFrontierSha256);
            Assert.NotEqual(afterSchemaWrite.HistoryContentSha256, afterHistoryWrite.HistoryContentSha256);
            Assert.Equal(afterSchemaWrite.HistoryRowCount + 1, afterHistoryWrite.HistoryRowCount);
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                databaseProbe = "negative sensitivity",
                businessWriteChangedLogFrontier = true,
                businessWriteChangedHistory = false,
                schemaWriteChangedLogFrontier = true,
                schemaWriteChangedHistory = false,
                historyWriteChangedLogFrontier = true,
                historyWriteChangedHistory = true
            }));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropEvidenceCatalogAsync(observerConfiguration.ConnectionString, catalog);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(_fixtureRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(_fixtureRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_fixtureRoot, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private void InitializeIsolatedOrganizationRepository(string repositoryRoot)
    {
        CopyDirectory(
            Path.Combine(repositoryRoot, "modules", "Organization"),
            Path.Combine(_fixtureRoot, "modules", "Organization"));
        CopyDirectory(
            Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Migrations"),
            Path.Combine(_fixtureRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Migrations"));
        CopyDirectory(
            Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "bin", "Release", "net10.0"),
            Path.Combine(_fixtureRoot, "server", "modules", "Organization", "Migrations.SqlServer", "bin", "Release", "net10.0"));
        CopyFile(
            Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Organization.SqlServerMigrations.csproj"),
            Path.Combine(_fixtureRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Organization.SqlServerMigrations.csproj"));
        CopyFile(
            Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json"),
            Path.Combine(_fixtureRoot, "server", "platform-modeling", "metadata", "storage-types.json"));
        CopyFile(
            Path.Combine(repositoryRoot, "apps", "host-api", "HostApi.csproj"),
            Path.Combine(_fixtureRoot, "apps", "host-api", "HostApi.csproj"));
        CopyDirectory(
            Path.Combine(repositoryRoot, "apps", "host-api", "bin", "Release", "net10.0"),
            Path.Combine(_fixtureRoot, "apps", "host-api", "bin", "Release", "net10.0"));
    }

    private static SqlServerObserverConnection ResolveSqlServerObserver(string repositoryRoot)
    {
        const string acceptanceVariable = "NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION";
        const string developmentVariable = "NEWP_DEV_SQLSERVER_CONNECTION_STRING";
        var configured = new[]
            {
                (Source: acceptanceVariable, Value: Environment.GetEnvironmentVariable(acceptanceVariable)),
                (Source: developmentVariable, Value: Environment.GetEnvironmentVariable(developmentVariable))
            }
            .FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate.Value));
        if (string.IsNullOrWhiteSpace(configured.Value))
        {
            var configurationPath = Path.Combine(repositoryRoot, "apps", "host-api", "appsettings.Development.json");
            if (File.Exists(configurationPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
                if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                    && connectionStrings.TryGetProperty("SqlServer", out var sqlServer)
                    && sqlServer.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(sqlServer.GetString()))
                {
                    configured = (
                        "apps/host-api/appsettings.Development.json:ConnectionStrings:SqlServer",
                        sqlServer.GetString());
                }
            }
        }

        if (string.IsNullOrWhiteSpace(configured.Value))
        {
            throw new InvalidOperationException(
                "MIGRATION_MANIFEST_DB_SNAPSHOT_CONNECTION_MISSING: configure an Acceptance or development SQL Server connection before enabling the read-only database evidence test.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(configured.Value)
            {
                InitialCatalog = "master",
                Pooling = false,
                ApplicationName = "NewP.MigrationManifest.ReadOnlyEvidence"
            };
            return new SqlServerObserverConnection(configured.Source, builder.ConnectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"MIGRATION_MANIFEST_DB_SNAPSHOT_CONNECTION_INVALID: source '{configured.Source}' is invalid.",
                exception);
        }
    }

    private static async Task<IReadOnlyList<ObservedDatabase>> ReadObservedDatabasesAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT databases.[database_id], databases.[name], databases.[create_date],
                   databases.[state], databases.[user_access], databases.[is_read_only],
                   databases.[recovery_model], databases.[compatibility_level],
                   COALESCE(databases.[collation_name], N''), HAS_DBACCESS(databases.[name])
            FROM sys.databases AS databases
            WHERE databases.[database_id] > 4
              AND databases.[state] = 0
            ORDER BY databases.[database_id];
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var databases = new List<ObservedDatabase>();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(9) || reader.GetInt32(9) != 1)
            {
                throw new InvalidOperationException(
                    $"MIGRATION_MANIFEST_DB_DATABASE_INACCESSIBLE: online user database id '{reader.GetInt32(0)}' is not accessible to the observer.");
            }

            var fields = Enumerable.Range(0, reader.FieldCount)
                .Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
            databases.Add(new ObservedDatabase(reader.GetInt32(0), reader.GetString(1), string.Join('\u001f', fields)));
        }

        if (databases.Count == 0)
        {
            throw new InvalidOperationException(
                "MIGRATION_MANIFEST_DB_SNAPSHOT_EMPTY: no accessible online user database was available to observe.");
        }

        return databases;
    }

    private static async Task<IReadOnlyList<string>> CaptureLogFrontiersAsync(
        SqlConnection connection,
        IReadOnlyList<ObservedDatabase> expectedDatabases)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT databases.[database_id], databases.[name], log_stats.[log_end_lsn]
            FROM sys.databases AS databases
            CROSS APPLY sys.dm_db_log_stats(databases.[database_id]) AS log_stats
            WHERE databases.[database_id] > 4
              AND databases.[state] = 0
            ORDER BY databases.[database_id];
            """;
        var actualDatabases = new List<(int Id, string Name, string LogEndLsn)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var logEndLsn = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(logEndLsn))
            {
                throw new InvalidOperationException(
                    $"MIGRATION_MANIFEST_DB_LOG_FRONTIER_MISSING: database id '{reader.GetInt32(0)}' has no log_end_lsn.");
            }

            actualDatabases.Add((reader.GetInt32(0), reader.GetString(1), logEndLsn));
        }

        if (!expectedDatabases.Select(static database => (database.Id, database.Name))
                .SequenceEqual(actualDatabases.Select(static database => (database.Id, database.Name))))
        {
            throw new InvalidOperationException(
                $"MIGRATION_MANIFEST_DB_SNAPSHOT_UNSTABLE: accessible user database ids [{string.Join(",", expectedDatabases.Select(static database => database.Id))}] " +
                $"did not match log frontier ids [{string.Join(",", actualDatabases.Select(static database => database.Id))}].");
        }

        return expectedDatabases.Zip(
                actualDatabases,
                static (database, log) => $"{database.Metadata}\u001f{log.LogEndLsn}")
            .ToArray();
    }

    private static async Task<DatabaseHistorySnapshot> CaptureMigrationHistoryAsync(
        SqlConnection connection,
        ObservedDatabase database)
    {
        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT schemas.[name]
            FROM sys.tables AS tables
            INNER JOIN sys.schemas AS schemas ON schemas.[schema_id] = tables.[schema_id]
            WHERE tables.[name] = N'__NewPMigrationHistory'
            ORDER BY schemas.[name];
            """;
        var schemas = new List<string>();
        await using (var tableReader = await tableCommand.ExecuteReaderAsync())
        {
            while (await tableReader.ReadAsync())
            {
                schemas.Add(tableReader.GetString(0));
            }
        }

        if (schemas.Count == 0)
        {
            return new DatabaseHistorySnapshot(0, 0, [$"{database.Id}\u001fnone"]);
        }

        long rowCount = 0;
        var facts = new List<string>();
        foreach (var schema in schemas)
        {
            var table = $"{QuoteIdentifier(schema)}.[__NewPMigrationHistory]";
            await using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = $"""
                SELECT
                    (SELECT COUNT_BIG(*) FROM {table}) AS [RowCount],
                    (SELECT [Position], [HistoryVersion], [MigrationId], [OwnerModuleId],
                            [BundleDigest], [ContentDigest], [FromModelDigest], [ToModelDigest], [PlanDigest],
                            [ProviderTargetVersion], [ProviderTargetDigest],
                            [ProviderTransitionVersion], [ProviderTransitionDigest]
                     FROM {table}
                     ORDER BY [Position], [MigrationId]
                     FOR JSON PATH, INCLUDE_NULL_VALUES) AS [CanonicalRows];
                """;
            await using var historyReader = await historyCommand.ExecuteReaderAsync();
            if (!await historyReader.ReadAsync())
            {
                throw new InvalidOperationException(
                    $"MIGRATION_MANIFEST_DB_HISTORY_UNREADABLE: database id '{database.Id}' history table returned no snapshot row.");
            }

            var tableRowCount = historyReader.GetInt64(0);
            var content = historyReader.IsDBNull(1) ? "[]" : historyReader.GetString(1);
            rowCount += tableRowCount;
            facts.Add($"{database.Id}\u001f{schema}\u001f{tableRowCount}\u001f{ComputeSha256([content])}");
        }

        return new DatabaseHistorySnapshot(schemas.Count, rowCount, facts);
    }

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string ComputeSha256(IEnumerable<string> values) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))));

    private static async Task CreateEvidenceCatalogAsync(string adminConnectionString, string catalog)
    {
        ValidateEvidenceCatalog(catalog);
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}];";
        await command.ExecuteNonQueryAsync();

        var catalogConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog,
            Pooling = false
        }.ConnectionString;
        await using var catalogConnection = new SqlConnection(catalogConnectionString);
        await catalogConnection.OpenAsync();
        await using var setup = catalogConnection.CreateCommand();
        setup.CommandText = """
            CREATE TABLE [dbo].[EvidenceBusiness]
            (
                [Id] int NOT NULL CONSTRAINT [PK_EvidenceBusiness] PRIMARY KEY,
                [Value] nvarchar(64) NOT NULL
            );
            CREATE TABLE [dbo].[__NewPMigrationHistory]
            (
                [Position] int NOT NULL,
                [HistoryVersion] nvarchar(64) NOT NULL,
                [MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK_EvidenceMigrationHistory] PRIMARY KEY,
                [OwnerModuleId] nvarchar(256) NOT NULL,
                [BundleDigest] char(64) NOT NULL,
                [ContentDigest] char(64) NOT NULL,
                [FromModelDigest] char(64) NOT NULL,
                [ToModelDigest] char(64) NOT NULL,
                [PlanDigest] char(64) NOT NULL,
                [ProviderTargetVersion] nvarchar(64) NOT NULL,
                [ProviderTargetDigest] char(64) NOT NULL,
                [ProviderTransitionVersion] nvarchar(64) NOT NULL,
                [ProviderTransitionDigest] char(64) NOT NULL
            );
            """;
        await setup.ExecuteNonQueryAsync();
    }

    private static async Task InjectBusinessWriteAsync(string adminConnectionString, string catalog)
    {
        await ExecuteEvidenceWriteAsync(
            adminConnectionString,
            catalog,
            "INSERT INTO [dbo].[EvidenceBusiness] ([Id], [Value]) VALUES (1, N'committed');");
    }

    private static async Task InjectSchemaWriteAsync(string adminConnectionString, string catalog)
    {
        await ExecuteEvidenceWriteAsync(
            adminConnectionString,
            catalog,
            "ALTER TABLE [dbo].[EvidenceBusiness] ADD [SchemaSentinel] bit NULL;");
    }

    private static async Task InjectHistoryWriteAsync(string adminConnectionString, string catalog)
    {
        await ExecuteEvidenceWriteAsync(
            adminConnectionString,
            catalog,
            """
            INSERT INTO [dbo].[__NewPMigrationHistory]
            (
                [Position], [HistoryVersion], [MigrationId], [OwnerModuleId],
                [BundleDigest], [ContentDigest], [FromModelDigest], [ToModelDigest], [PlanDigest],
                [ProviderTargetVersion], [ProviderTargetDigest],
                [ProviderTransitionVersion], [ProviderTransitionDigest]
            )
            VALUES
            (
                1, N'1.0', N'20990101000000_Evidence_000001_Committed', N'Evidence',
                REPLICATE('a', 64), REPLICATE('b', 64), REPLICATE('c', 64), REPLICATE('d', 64), REPLICATE('e', 64),
                N'1.0', REPLICATE('f', 64), N'1.0', REPLICATE('0', 64)
            );
            """);
    }

    private static async Task ExecuteEvidenceWriteAsync(
        string adminConnectionString,
        string catalog,
        string commandText)
    {
        ValidateEvidenceCatalog(catalog);
        var connectionString = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = catalog,
            Pooling = false
        }.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropEvidenceCatalogAsync(string adminConnectionString, string catalog)
    {
        ValidateEvidenceCatalog(catalog);
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{catalog}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{catalog}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateEvidenceCatalog(string catalog)
    {
        if (!catalog.StartsWith("NewPMigrationManifestEvidence_", StringComparison.Ordinal)
            || catalog.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException("Unsafe evidence catalog name.");
        }
    }

    private static IReadOnlyList<string> GetProtectedAssetPaths(string repositoryRoot) =>
    [
        Path.Combine(repositoryRoot, "modules", "Organization", "migrations", "Organization.SqlServerMigrations.json"),
        Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "bin", "Release", "net10.0", "Organization.SqlServerMigrations.dll"),
        Path.Combine(repositoryRoot, "apps", "host-api", "bin", "Release", "net10.0", "modules", "Organization", "migrations", "Organization.SqlServerMigrations.json"),
        Path.Combine(repositoryRoot, "apps", "host-api", "bin", "Release", "net10.0", "modules", "Organization", "migrations", "Organization.SqlServerMigrations.dll")
    ];

    private static IReadOnlyDictionary<string, string> SnapshotFiles(IEnumerable<string> paths) =>
        paths.ToDictionary(
            static path => path,
            static path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal);

    private GitSnapshot CaptureGitSnapshot()
    {
        var head = RunGit("rev-parse", "HEAD").Trim();
        var indexTree = RunGit("write-tree").Trim();
        var status = RunGit("status", "--porcelain=v2", "--untracked-files=all")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var trackedContent = RunGit("ls-files", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path}={Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_fixtureRoot, path))))}");
        var trackedContentSha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", trackedContent))));
        return new GitSnapshot(head, indexTree, status, trackedContentSha256);
    }

    private void InitializeIsolatedGitRepository()
    {
        File.WriteAllText(
            Path.Combine(_fixtureRoot, ".gitignore"),
            "*\r\n!.gitignore\r\n!staged-sentinel.txt\r\n!unstaged-sentinel.txt\r\n");
        var stagedSentinel = Path.Combine(_fixtureRoot, "staged-sentinel.txt");
        var unstagedSentinel = Path.Combine(_fixtureRoot, "unstaged-sentinel.txt");
        File.WriteAllText(stagedSentinel, "baseline\r\n");
        File.WriteAllText(unstagedSentinel, "baseline\r\n");
        RunGit("init");
        RunGit("config", "user.name", "Migration Manifest Test");
        RunGit("config", "user.email", "migration-manifest@example.invalid");
        RunGit("add", ".gitignore", "staged-sentinel.txt", "unstaged-sentinel.txt");
        RunGit("commit", "-m", "isolated baseline");
        File.WriteAllText(stagedSentinel, "staged change\r\n");
        RunGit("add", "staged-sentinel.txt");
        File.WriteAllText(unstagedSentinel, "unstaged change\r\n");
    }

    private string RunGit(params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _fixtureRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start(), "Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit {process.ExitCode}: {error}");
        return output;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            CopyFile(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

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

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record GitSnapshot(
        string Head,
        string IndexTree,
        string Status,
        string TrackedContentSha256);

    private sealed record ObservedDatabase(int Id, string Name, string Metadata);

    private sealed record DatabaseHistorySnapshot(int TableCount, long RowCount, IReadOnlyList<string> Facts);

    private sealed record DatabaseSnapshot(
        int DatabaseCount,
        string LogFrontierSha256,
        string LogFrontierEvidence,
        int HistoryTableCount,
        long HistoryRowCount,
        string HistoryContentSha256);

    private sealed class SqlServerDatabaseObserver : IAsyncDisposable
    {
        private readonly SqlConnection _masterConnection;
        private readonly IReadOnlyList<ObservedDatabase> _databases;
        private readonly IReadOnlyDictionary<int, SqlConnection> _databaseConnections;

        private SqlServerDatabaseObserver(
            SqlConnection masterConnection,
            IReadOnlyList<ObservedDatabase> databases,
            IReadOnlyDictionary<int, SqlConnection> databaseConnections)
        {
            _masterConnection = masterConnection;
            _databases = databases;
            _databaseConnections = databaseConnections;
        }

        internal static async Task<SqlServerDatabaseObserver> OpenAsync(string masterConnectionString)
        {
            var masterConnection = new SqlConnection(masterConnectionString);
            var databaseConnections = new Dictionary<int, SqlConnection>();
            try
            {
                await masterConnection.OpenAsync();
                var databases = await ReadObservedDatabasesAsync(masterConnection);
                foreach (var database in databases)
                {
                    var builder = new SqlConnectionStringBuilder(masterConnectionString)
                    {
                        InitialCatalog = database.Name,
                        Pooling = false,
                        ApplicationName = "NewP.MigrationManifest.ReadOnlyEvidence"
                    };
                    var databaseConnection = new SqlConnection(builder.ConnectionString);
                    await databaseConnection.OpenAsync();
                    databaseConnections.Add(database.Id, databaseConnection);
                }

                return new SqlServerDatabaseObserver(masterConnection, databases, databaseConnections);
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                foreach (var connection in databaseConnections.Values)
                {
                    await connection.DisposeAsync();
                }

                await masterConnection.DisposeAsync();
                if (exception is InvalidOperationException invalidOperation
                    && invalidOperation.Message.StartsWith("MIGRATION_MANIFEST_DB_", StringComparison.Ordinal))
                {
                    throw;
                }

                throw new InvalidOperationException(
                    "MIGRATION_MANIFEST_DB_SNAPSHOT_UNAVAILABLE: could not open every accessible online user database for read-only observation.");
            }
        }

        internal async Task<DatabaseSnapshot> CaptureAsync()
        {
            try
            {
                var historyFacts = new List<string>();
                var historyTableCount = 0;
                long historyRowCount = 0;
                foreach (var database in _databases)
                {
                    var history = await CaptureMigrationHistoryAsync(_databaseConnections[database.Id], database);
                    historyTableCount += history.TableCount;
                    historyRowCount += history.RowCount;
                    historyFacts.AddRange(history.Facts);
                }

                var logFrontiers = await CaptureLogFrontiersAsync(_masterConnection, _databases);
                var logFrontierEvidence = string.Join(
                    ",",
                    _databases.Zip(
                        logFrontiers,
                        static (database, frontier) => $"{database.Id}:{ComputeSha256([frontier])}"));
                return new DatabaseSnapshot(
                    _databases.Count,
                    ComputeSha256(logFrontiers),
                    logFrontierEvidence,
                    historyTableCount,
                    historyRowCount,
                    ComputeSha256(historyFacts));
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                if (exception is InvalidOperationException invalidOperation
                    && invalidOperation.Message.StartsWith("MIGRATION_MANIFEST_DB_", StringComparison.Ordinal))
                {
                    throw;
                }

                throw new InvalidOperationException(
                    "MIGRATION_MANIFEST_DB_SNAPSHOT_UNAVAILABLE: could not read a complete log-frontier and migration-history snapshot.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in _databaseConnections.Values)
            {
                await connection.DisposeAsync();
            }

            await _masterConnection.DisposeAsync();
        }
    }

    private sealed class SqlServerObserverConnection
    {
        internal SqlServerObserverConnection(string source, string connectionString)
        {
            Source = source;
            ConnectionString = connectionString;
        }

        internal string Source { get; }

        internal string ConnectionString { get; }
    }
}
