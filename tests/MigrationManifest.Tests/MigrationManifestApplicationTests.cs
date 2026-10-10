using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Tools.MigrationManifest;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ManifestDocument = ICP.Platform.Persistence.Planning.MigrationManifest;

namespace ICP.Platform.Tools.MigrationManifest.Tests;

public sealed class MigrationManifestApplicationTests
{
    [Fact]
    public void Cli_FinalizeAndCheck_ExposeRequiredCommands()
    {
        using var fixture = MigrationManifestFixture.Create();
        var output = new StringWriter();
        var error = new StringWriter();
        var finalizeExit = MigrationManifestCli.Run(
            [
                "finalize",
                "--module", fixture.ModuleId,
                "--provider", "SqlServer",
                "--migration", MigrationManifestFixture.TailMigrationId,
                "--compatibility", "safe-expand",
                "--repository-root", fixture.Root
            ],
            output,
            error);
        File.Copy(fixture.ManifestPath, fixture.HostManifestPath, overwrite: true);
        var checkExit = MigrationManifestCli.Run(
            ["check", "--all", "--no-build", "--repository-root", fixture.Root],
            output,
            error);

        Assert.Equal(0, finalizeExit);
        Assert.Equal(0, checkExit);
        Assert.Contains("finalized Fixture/SqlServer", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("check passed: 1 bundle(s)", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Finalize_NewTail_WritesCanonicalDerivedFactsAndIsIdempotent()
    {
        using var fixture = MigrationManifestFixture.Create();
        var application = new MigrationManifestApplication(fixture.Root);

        var first = application.Finalize(
            fixture.ModuleId,
            "SqlServer",
            MigrationManifestFixture.TailMigrationId,
            "contract");
        var firstBytes = File.ReadAllBytes(fixture.ManifestPath);
        var second = application.Finalize(
            fixture.ModuleId,
            "SqlServer",
            MigrationManifestFixture.TailMigrationId,
            compatibility: null);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(firstBytes, File.ReadAllBytes(fixture.ManifestPath));
        var manifest = MigrationManifestIntegrity.Parse(firstBytes);
        Assert.Equal(MigrationManifestFixture.TailMigrationId, manifest.Migrations[^1].MigrationId);
        Assert.Equal("000003", manifest.Migrations[^1].Sequence);
        Assert.Equal("contract", manifest.Migrations[^1].Compatibility);
        Assert.Equal(manifest.Migrations[^2].ContentDigest, manifest.Migrations[^1].PreviousContentDigest);
        Assert.Equal(manifest.Migrations[^2].ToModelDigest, manifest.Migrations[^1].FromModelDigest);
        Assert.Equal(manifest.ModelDigest, manifest.Migrations[^1].ToModelDigest);
        Assert.Equal(
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(
                File.ReadAllText(fixture.TailSourcePath)),
            manifest.Migrations[^1].ContentDigest);
        Assert.Equal(
            MigrationManifestIntegrity.ComputeBundleChecksum(File.ReadAllBytes(fixture.BundlePath)),
            manifest.BundleChecksum);
        Assert.Equal(firstBytes, MigrationManifestIntegrity.SerializeCanonical(manifest));
    }

    [Theory]
    [InlineData("missing-compatibility", "MIGRATION_MANIFEST_COMPATIBILITY_REQUIRED")]
    [InlineData("invalid-compatibility", "MIGRATION_MANIFEST_COMPATIBILITY_INVALID")]
    [InlineData("history-source", "MIGRATION_MANIFEST_HISTORY_CHANGED")]
    [InlineData("history-compatibility", "MIGRATION_MANIFEST_HISTORY_CHANGED")]
    [InlineData("history-model-transition", "MIGRATION_MANIFEST_HISTORY_CHANGED")]
    [InlineData("broken-chain", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    [InlineData("same-name", "MIGRATION_MANIFEST_NON_TAIL")]
    [InlineData("wrong-tail", "MIGRATION_MANIFEST_NON_TAIL")]
    [InlineData("multiple-new-types", "MIGRATION_MANIFEST_NON_TAIL")]
    [InlineData("undeclared-provider", "MIGRATION_MANIFEST_BUNDLE_NOT_DECLARED")]
    public void Finalize_RejectedInput_DoesNotChangeManifest(string mutation, string expectedCode)
    {
        using var fixture = MigrationManifestFixture.Create();
        var application = new MigrationManifestApplication(fixture.Root);
        var migrationId = MigrationManifestFixture.TailMigrationId;
        var provider = "SqlServer";
        string? compatibility = "safe-expand";
        switch (mutation)
        {
            case "missing-compatibility": compatibility = null; break;
            case "invalid-compatibility": compatibility = "unreviewed"; break;
            case "history-source": File.AppendAllText(fixture.FoundationSourcePath, "changed"); break;
            case "history-compatibility":
                fixture.RewriteManifest(manifest => manifest with
                {
                    Migrations = [manifest.Migrations[0] with { Compatibility = "contract" }, manifest.Migrations[1]]
                }); break;
            case "history-model-transition":
                fixture.RewriteManifest(manifest => manifest with
                {
                    Migrations =
                    [
                        manifest.Migrations[0] with { ToModelDigest = new string('a', 64) },
                        manifest.Migrations[1] with { FromModelDigest = new string('a', 64) }
                    ]
                }); break;
            case "broken-chain":
                fixture.RewriteManifest(manifest => manifest with
                {
                    Migrations = [manifest.Migrations[0] with { PreviousContentDigest = new string('f', 64) }]
                }); break;
            case "same-name": migrationId = MigrationManifestFixture.FoundationMigrationId; break;
            case "wrong-tail": migrationId = "20990101000000_Fixture_000003_Absent"; break;
            case "multiple-new-types": fixture.RewriteManifest(manifest => manifest with { Migrations = [manifest.Migrations[0]] }); break;
            case "undeclared-provider": provider = "Oracle"; break;
        }

        var before = File.ReadAllBytes(fixture.ManifestPath);
        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            application.Finalize(fixture.ModuleId, provider, migrationId, compatibility));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        if (mutation == "history-compatibility")
        {
            Assert.Contains("field='/migrations/0/compatibility'", exception.Message, StringComparison.Ordinal);
        }

        if (mutation == "history-model-transition")
        {
            Assert.Contains("field='/migrations/0/toModelDigest'", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Finalize_TrackedRepositoryUsesHeadBaselineWhenHostCopyContainsTheSameDrift()
    {
        using var fixture = MigrationManifestFixture.Create();
        fixture.CommitPublishedBaseline();
        fixture.RewriteManifest(
            manifest => manifest with
            {
                Migrations = [manifest.Migrations[0] with { Compatibility = "contract" }, manifest.Migrations[1]]
            },
            synchronizeHost: true);
        var before = File.ReadAllBytes(fixture.ManifestPath);

        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            new MigrationManifestApplication(fixture.Root).Finalize(
                fixture.ModuleId,
                "SqlServer",
                MigrationManifestFixture.TailMigrationId,
                "safe-expand"));

        Assert.Equal("MIGRATION_MANIFEST_HISTORY_CHANGED", exception.Code);
        Assert.Contains("git:HEAD:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field='/migrations/0/compatibility'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
    }

    [Fact]
    public void Finalize_PublishedModuleWithoutTrackedManifest_FailsClosed()
    {
        using var fixture = MigrationManifestFixture.Create();
        var bytes = File.ReadAllBytes(fixture.ManifestPath);
        File.Delete(fixture.ManifestPath);
        fixture.CommitPublishedBaseline();
        File.WriteAllBytes(fixture.ManifestPath, bytes);
        var failure = Assert.Throws<MigrationManifestToolException>(() => new MigrationManifestApplication(fixture.Root).Finalize(fixture.ModuleId, "SqlServer", MigrationManifestFixture.TailMigrationId, "safe-expand"));
        Assert.Equal("MIGRATION_MANIFEST_PUBLISHED_BASELINE_MISSING", failure.Code);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.ManifestPath));
    }

    [Fact]
    public void Finalize_IsolatedRootWithoutFrozenHostBaselineFailsClosed()
    {
        using var fixture = MigrationManifestFixture.Create();
        File.Delete(fixture.HostManifestPath);
        var before = File.ReadAllBytes(fixture.ManifestPath);

        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            new MigrationManifestApplication(fixture.Root).Finalize(
                fixture.ModuleId,
                "SqlServer",
                MigrationManifestFixture.TailMigrationId,
                "safe-expand"));

        Assert.Equal("MIGRATION_MANIFEST_PUBLISHED_BASELINE_MISSING", exception.Code);
        Assert.Contains("field='publishedPrefix'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(1175)]
    public void Finalize_TransientAtomicReplaceFailure_RetriesOnceThenSucceeds(int win32Error)
    {
        using var fixture = MigrationManifestFixture.Create();
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var application = new MigrationManifestApplication(
            fixture.Root,
            replaceFile: (temporaryPath, manifestPath) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw CreateWin32IOException(win32Error);
                }

                File.Replace(temporaryPath, manifestPath, destinationBackupFileName: null);
            },
            retryDelay: delays.Add);

        var result = application.Finalize(
            fixture.ModuleId,
            "SqlServer",
            MigrationManifestFixture.TailMigrationId,
            "safe-expand");

        Assert.True(result.Changed);
        Assert.InRange(attempts, 2, 4);
        Assert.Equal(attempts - 1, delays.Count);
        Assert.Equal("000003", MigrationManifestIntegrity.Parse(File.ReadAllBytes(fixture.ManifestPath)).Migrations[^1].Sequence);
    }

    [Fact]
    public void Finalize_TransientAtomicReplaceFailure_StopsAfterFourAttemptsAndPreservesOriginalManifest()
    {
        using var fixture = MigrationManifestFixture.Create();
        var before = File.ReadAllBytes(fixture.ManifestPath);
        var failure = CreateWin32IOException(1175);
        var attempts = 0;
        var application = new MigrationManifestApplication(
            fixture.Root,
            replaceFile: (_, _) =>
            {
                attempts++;
                throw failure;
            },
            retryDelay: static _ => { });

        var thrown = Assert.Throws<IOException>(() => application.Finalize(
            fixture.ModuleId,
            "SqlServer",
            MigrationManifestFixture.TailMigrationId,
            "safe-expand"));

        Assert.Same(failure, thrown);
        Assert.Equal(4, attempts);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.ManifestPath)!, ".migration-manifest-*.tmp"));
    }

    [Fact]
    public void Finalize_NonTransientAtomicReplaceFailure_DoesNotRetryAndPreservesOriginalManifest()
    {
        using var fixture = MigrationManifestFixture.Create();
        var before = File.ReadAllBytes(fixture.ManifestPath);
        var failure = CreateWin32IOException(1176);
        var attempts = 0;
        var application = new MigrationManifestApplication(
            fixture.Root,
            replaceFile: (_, _) =>
            {
                attempts++;
                throw failure;
            },
            retryDelay: static _ => throw new InvalidOperationException("non-transient failures must not delay"));

        var thrown = Assert.Throws<IOException>(() => application.Finalize(
            fixture.ModuleId,
            "SqlServer",
            MigrationManifestFixture.TailMigrationId,
            "safe-expand"));

        Assert.Same(failure, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.ManifestPath)!, ".migration-manifest-*.tmp"));
    }

    private static IOException CreateWin32IOException(int errorCode) =>
        new(
            $"injected Win32 replace failure {errorCode}",
            unchecked((int)(0x80070000u | (uint)errorCode)));

    [Fact]
    public void Finalize_ExistingTailRejectsCompatibilityRewrite()
    {
        using var fixture = MigrationManifestFixture.Create(finalized: true);
        var before = File.ReadAllBytes(fixture.ManifestPath);

        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            new MigrationManifestApplication(fixture.Root).Finalize(
                fixture.ModuleId,
                "SqlServer",
                MigrationManifestFixture.TailMigrationId,
                "contract"));

        Assert.Equal("MIGRATION_MANIFEST_HISTORY_CHANGED", exception.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
    }

    [Theory]
    [InlineData("model", "MIGRATION_MANIFEST_MODEL_DRIFT")]
    [InlineData("source", "MIGRATION_MANIFEST_SOURCE_DRIFT")]
    [InlineData("chain", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    [InlineData("bundle", "PERSISTENCE_BUNDLE_CHECKSUM_MISMATCH")]
    [InlineData("host-dll", "MIGRATION_MANIFEST_HOST_COPY_DRIFT")]
    [InlineData("host-manifest", "MIGRATION_MANIFEST_HOST_COPY_DRIFT")]
    [InlineData("type", "PERSISTENCE_MIGRATION_MISSING")]
    [InlineData("undeclared-type", "PERSISTENCE_MIGRATION_UNDECLARED")]
    public void CheckAll_Drift_ReturnsStableLocatedFailure(string mutation, string expectedCode)
    {
        using var fixture = MigrationManifestFixture.Create(finalized: true);
        switch (mutation)
        {
            case "model":
                File.WriteAllText(
                fixture.ModelPath,
                File.ReadAllText(fixture.ModelPath).Replace("RecordSource", "RecordSourceChanged", StringComparison.Ordinal)); break;
            case "source": File.AppendAllText(fixture.TailSourcePath, "changed"); break;
            case "chain":
                fixture.RewriteManifest(manifest => manifest with
                {
                    Migrations = [manifest.Migrations[0], manifest.Migrations[1] with { PreviousContentDigest = new string('f', 64) }]
                }, synchronizeHost: true); break;
            case "bundle": AppendByte(fixture.BundlePath); break;
            case "host-dll": AppendByte(fixture.HostBundlePath); break;
            case "host-manifest": File.AppendAllText(fixture.HostManifestPath, " "); break;
            case "type": fixture.ReplaceTailWithMissingType(); break;
            case "undeclared-type": fixture.RemoveTailFromManifest(); break;
        }

        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            new MigrationManifestApplication(fixture.Root).CheckAll(noBuild: true));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Contains(fixture.ModuleId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SqlServer", exception.Message, StringComparison.Ordinal);
    }

    private static void AppendByte(string path)
    {
        using var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.None);
        stream.WriteByte(0);
    }

    [Fact]
    public void CheckAll_NoBuildMissingAsset_ProvidesBuildCommand()
    {
        using var fixture = MigrationManifestFixture.Create(finalized: true);
        File.Delete(fixture.BundlePath);

        var exception = Assert.Throws<MigrationManifestToolException>(() =>
            new MigrationManifestApplication(fixture.Root).CheckAll(noBuild: true));

        Assert.Equal("MIGRATION_MANIFEST_RELEASE_ASSET_MISSING", exception.Code);
        Assert.Contains("dotnet build", exception.Message, StringComparison.Ordinal);
        Assert.Contains("-c Release", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAll_ValidAssets_IsReadOnlyAndReportsProviderMatrix()
    {
        using var fixture = MigrationManifestFixture.Create(finalized: true);
        var before = fixture.SnapshotFiles();

        var result = new MigrationManifestApplication(fixture.Root).CheckAll(noBuild: true);

        Assert.Equal(before, fixture.SnapshotFiles());
        Assert.Equal(1, result.CheckedBundles);
        Assert.Contains("SqlServer: 1 bundle checked", result.ProviderResults);
        Assert.Contains("Oracle: no declared bundle", result.ProviderResults);
        Assert.Contains("MySql: no declared bundle", result.ProviderResults);
    }
}

internal sealed class MigrationManifestFixture : IDisposable
{
    internal const string FoundationMigrationId = "20260903000000_Fixture_000001_Foundation";
    internal const string IntermediateMigrationId = "20260903000001_Fixture_000002_Intermediate";
    internal const string TailMigrationId = "20260903000002_Fixture_000003_Tail";

    private MigrationManifestFixture(string root)
    {
        Root = root;
    }

    internal string Root { get; }

    internal string ModuleId => "Fixture";

    internal string ModuleRoot => Path.Combine(Root, "modules", ModuleId);

    internal string ManifestPath => Path.Combine(ModuleRoot, "migrations", "MigrationManifest.Tests.json");

    internal string BundlePath => Path.Combine(
        Root,
        "server",
        "modules",
        ModuleId,
        "Migrations.SqlServer",
        "bin",
        "Release",
        "net10.0",
        "MigrationManifest.Tests.dll");

    internal string FoundationSourcePath => Path.Combine(
        Root,
        "server",
        "modules",
        ModuleId,
        "Migrations.SqlServer",
        "Migrations",
        FoundationMigrationId + ".cs");

    internal string TailSourcePath => Path.Combine(
        Root,
        "server",
        "modules",
        ModuleId,
        "Migrations.SqlServer",
        "Migrations",
        TailMigrationId + ".cs");

    internal string IntermediateSourcePath => Path.Combine(
        Root,
        "server",
        "modules",
        ModuleId,
        "Migrations.SqlServer",
        "Migrations",
        IntermediateMigrationId + ".cs");

    internal string ModelPath => Path.Combine(ModuleRoot, "metadata", "models", "ExampleRecord.model.json");

    internal string HostRoot => Path.Combine(Root, "apps", "host-api", "bin", "Release", "net10.0");

    internal string HostBundlePath => Path.Combine(HostRoot, "modules", ModuleId, "migrations", "MigrationManifest.Tests.dll");

    internal string HostManifestPath => Path.Combine(HostRoot, "modules", ModuleId, "migrations", "MigrationManifest.Tests.json");

    internal static MigrationManifestFixture Create(bool finalized = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "newp-migration-manifest-tests", Guid.NewGuid().ToString("N"));
        var fixture = new MigrationManifestFixture(root);
        fixture.Initialize();
        if (finalized)
        {
            new MigrationManifestApplication(root).Finalize(
                fixture.ModuleId,
                "SqlServer",
                TailMigrationId,
                "safe-expand");
            File.Copy(fixture.ManifestPath, fixture.HostManifestPath, overwrite: true);
        }

        return fixture;
    }

    internal IReadOnlyDictionary<string, string> SnapshotFiles() =>
        Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(Root, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    internal void RewriteManifest(
        Func<ManifestDocument, ManifestDocument> rewrite,
        bool synchronizeHost = false)
    {
        var manifest = rewrite(MigrationManifestIntegrity.Parse(File.ReadAllBytes(ManifestPath)));
        File.WriteAllBytes(ManifestPath, MigrationManifestIntegrity.SerializeCanonical(manifest));
        if (synchronizeHost)
        {
            File.Copy(ManifestPath, HostManifestPath, overwrite: true);
        }
    }

    internal void ReplaceTailWithMissingType()
    {
        const string missingId = "20260903000002_Fixture_000003_Missing";
        var missingSource = Path.Combine(Path.GetDirectoryName(TailSourcePath)!, missingId + ".cs");
        File.WriteAllText(missingSource, "missing type source\r\n");
        RewriteManifest(manifest => manifest with
        {
            Migrations =
            [
                manifest.Migrations[0],
                manifest.Migrations[1] with
                {
                    MigrationId = missingId,
                    ContentDigest = MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(missingSource))
                }
            ]
        }, synchronizeHost: true);
    }

    internal void CommitPublishedBaseline()
    {
        RunGit("init", "--quiet");
        RunGit("add", "--all");
        RunGit(
            "-c", "user.name=Migration Manifest Tests",
            "-c", "user.email=migration-manifest-tests@example.invalid",
            "commit", "--quiet", "-m", "published baseline");
    }

    internal void RemoveTailFromManifest() =>
        RewriteManifest(
            manifest => manifest with { Migrations = [manifest.Migrations[0], manifest.Migrations[1]] },
            synchronizeHost: true);

    private void Initialize()
    {
        var repositoryRoot = FindRepositoryRoot();
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(FoundationSourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(BundlePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(HostBundlePath)!);
        Directory.CreateDirectory(Path.Combine(HostRoot, "modules", ModuleId, "metadata", "models"));
        Directory.CreateDirectory(Path.Combine(HostRoot, "server", "platform-modeling", "metadata"));
        Directory.CreateDirectory(Path.Combine(Root, "server", "platform-modeling", "metadata"));

        File.Copy(
            Path.Combine(repositoryRoot, "modules", "Example", "metadata", "models", "ExampleRecord.model.json"),
            ModelPath);
        File.Copy(
            Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json"),
            Path.Combine(HostRoot, "server", "platform-modeling", "metadata", "storage-types.json"));
        File.Copy(
            Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json"),
            Path.Combine(Root, "server", "platform-modeling", "metadata", "storage-types.json"));
        File.WriteAllText(FoundationSourcePath, "foundation source\r\n");
        File.WriteAllText(IntermediateSourcePath, "intermediate source\r\n");
        File.WriteAllText(TailSourcePath, "tail source\r\n");
        File.WriteAllText(
            Path.Combine(Root, "apps", "host-api", "HostApi.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\r\n");

        var testAssembly = typeof(MigrationManifestApplicationTests).Assembly.Location;
        File.Copy(testAssembly, BundlePath);
        File.Copy(testAssembly, HostBundlePath);
        File.Copy(testAssembly, Path.Combine(HostRoot, "MigrationManifest.Tests.dll"));

        var projectPath = Path.Combine(
            Root,
            "server",
            "modules",
            ModuleId,
            "Migrations.SqlServer",
            "MigrationManifest.Tests.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
            "<AssemblyName>MigrationManifest.Tests</AssemblyName><Deterministic>true</Deterministic>" +
            "<PathMap>$(MSBuildProjectDirectory)=/_/MigrationManifest.Tests</PathMap>" +
            "<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>" +
            "</PropertyGroup></Project>\r\n");

        var capsule = new
        {
            schemaVersion = "1.0",
            moduleId = ModuleId,
            displayName = "Fixture",
            version = "1.0.0",
            entryAssembly = "../../MigrationManifest.Tests.dll",
            dependencies = Array.Empty<string>(),
            backend = new { behaviorKey = "fixture.backend" },
            frontend = new { enabled = false },
            security = new { permissions = Array.Empty<object>() },
            persistence = new
            {
                models = new[] { "metadata/models/ExampleRecord.model.json" },
                migrations = new
                {
                    sqlServer = new
                    {
                        assembly = "migrations/MigrationManifest.Tests.dll",
                        manifest = "migrations/MigrationManifest.Tests.json"
                    }
                }
            }
        };
        var capsuleBytes = JsonSerializer.SerializeToUtf8Bytes(capsule, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllBytes(Path.Combine(ModuleRoot, "newp.plugin.json"), capsuleBytes);
        File.WriteAllBytes(Path.Combine(HostRoot, "modules", ModuleId, "plugin.json"), capsuleBytes);
        // 隔离工具发布物也显式声明组合角色，既有 fixture 模块仍独立拥有唯一迁移流。
        var composition = new
        {
            schemaVersion = "1.0", moduleId = "FixtureComposition", displayName = "Fixture composition", version = "1.0.0",
            composition = new { members = new[] { ModuleId } }, frontend = new { enabled = false }
        };
        var compositionBytes = JsonSerializer.SerializeToUtf8Bytes(composition);
        var authoringCompositionRoot = Path.Combine(Root, "modules", "FixtureComposition");
        var hostCompositionRoot = Path.Combine(HostRoot, "modules", "FixtureComposition");
        Directory.CreateDirectory(authoringCompositionRoot);
        Directory.CreateDirectory(hostCompositionRoot);
        File.WriteAllBytes(Path.Combine(authoringCompositionRoot, "newp.plugin.json"), compositionBytes);
        File.WriteAllBytes(Path.Combine(hostCompositionRoot, "plugin.json"), compositionBytes);
        File.WriteAllText(Path.Combine(HostRoot, "module-composition.json"),
            JsonSerializer.Serialize(new { platformCompositionModuleId = "FixtureComposition" }));
        File.Copy(ModelPath, Path.Combine(HostRoot, "modules", ModuleId, "metadata", "models", "ExampleRecord.model.json"));

        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(
            Path.Combine(HostRoot, "server", "platform-modeling", "metadata", "storage-types.json"));
        var document = new JsonModelMetadataDocumentLoader().LoadFromFile(ModelPath);
        var model = new ModelMetadataProjector().Project(document, typeMap, "SqlServer") with
        {
            OwnerModuleId = ModuleId,
            SchemaName = "dbo"
        };
        var modelDigest = PersistenceModelDigest.Compute([model]);
        var foundationDigest = MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(FoundationSourcePath));
        var intermediateDigest = MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(IntermediateSourcePath));
        var manifest = new ManifestDocument(
            "1.0",
            "SqlServer",
            ModuleId,
            modelDigest,
            new string('0', 64),
            [
                new MigrationManifestStep(
                    "000001",
                    FoundationMigrationId,
                    null,
                    foundationDigest,
                    new string('0', 64),
                    modelDigest,
                    "safe-expand"),
                new MigrationManifestStep(
                    "000002",
                    IntermediateMigrationId,
                    foundationDigest,
                    intermediateDigest,
                    modelDigest,
                    modelDigest,
                    "safe-expand")
            ]);
        File.WriteAllBytes(ManifestPath, MigrationManifestIntegrity.SerializeCanonical(manifest));
        File.Copy(ManifestPath, HostManifestPath);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server"))
                && Directory.Exists(Path.Combine(current.FullName, "modules")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private void RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git for the manifest fixture.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    public void Dispose()
    {
        var gitRoot = Path.Combine(Root, ".git");
        if (Directory.Exists(gitRoot))
        {
            foreach (var path in Directory.GetFiles(gitRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        Directory.Delete(Root, recursive: true);
    }
}

[Migration(MigrationManifestFixture.FoundationMigrationId)]
public sealed class FixtureFoundationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[Migration(MigrationManifestFixture.TailMigrationId)]
public sealed class FixtureTailMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[Migration(MigrationManifestFixture.IntermediateMigrationId)]
public sealed class FixtureIntermediateMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
