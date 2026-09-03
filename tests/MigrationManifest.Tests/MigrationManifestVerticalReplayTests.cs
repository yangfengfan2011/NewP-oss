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
        "SQL Server master 上的只读 catalog 前后快照；配置来源与 Acceptance admin connection 一致")]
    [Trait("Category", "SqlServerReadOnlyEvidence")]
    public async Task CheckAll_WithSqlServerReadOnlyObserver_PreservesDatabaseState()
    {
        var repositoryRoot = FindRepositoryRoot();
        InitializeIsolatedOrganizationRepository(repositoryRoot);
        var observer = ResolveSqlServerObserver(repositoryRoot);
        var before = await CaptureDatabaseSnapshotAsync(observer.ConnectionString);

        var check = new MigrationManifestApplication(_fixtureRoot).CheckAll(noBuild: true);

        var after = await CaptureDatabaseSnapshotAsync(observer.ConnectionString);
        Assert.Equal(before, after);
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            databaseProbe = "sys.databases read-only snapshot",
            connectionSource = observer.Source,
            catalogCount = after.CatalogCount,
            databaseStateSha256 = after.Sha256,
            checkedBundles = check.CheckedBundles,
            unchanged = true
        }));
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

    private static async Task<DatabaseSnapshot> CaptureDatabaseSnapshotAsync(string connectionString)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT [database_id], [name], [create_date], [state], [user_access],
                       [is_read_only], [recovery_model], [compatibility_level], COALESCE([collation_name], N'')
                FROM sys.databases
                ORDER BY [database_id];
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var fields = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
                rows.Add(string.Join('\u001f', fields));
            }

            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", rows));
            return new DatabaseSnapshot(rows.Count, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "MIGRATION_MANIFEST_DB_SNAPSHOT_UNAVAILABLE: the configured SQL Server read-only observer could not capture sys.databases.",
                exception);
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

    private sealed record DatabaseSnapshot(int CatalogCount, string Sha256);

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
