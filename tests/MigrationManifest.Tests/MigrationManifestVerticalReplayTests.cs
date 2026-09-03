using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Tools.MigrationManifest;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Tools.MigrationManifest.Tests;

public sealed class MigrationManifestVerticalReplayTests : IDisposable
{
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

        var check = application.CheckAll(noBuild: true);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(firstHash, secondHash);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(1, check.CheckedBundles);
        Assert.Contains("SqlServer: 1 bundle checked", check.ProviderResults);
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
            protectedAssetsUnchanged = true
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
}
