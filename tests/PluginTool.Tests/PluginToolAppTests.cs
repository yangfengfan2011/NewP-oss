using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginTool;

namespace ICP.Platform.PluginTool.Tests;

public sealed class PluginToolAppTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("newp_tool_").FullName;

    [Fact]
    public async Task RunAsync_FinalizeMigration_RejectsBundleWithoutRequestedTypeWithoutWritingManifest()
    {
        CreateFixture();
        var manifestPath = Path.Combine(_root, "migrations", "sqlserver", "migrations.json");
        File.Copy(typeof(PluginToolApp).Assembly.Location, Path.Combine(_root, "migrations", "sqlserver", "Partner.AssetCatalog.Migrations.dll"), overwrite: true);
        var original = File.ReadAllBytes(manifestPath);
        var stdout = new StringWriter();

        var exitCode = await PluginToolApp.RunAsync(
            ["plugin", "finalize-migration", "--root", _root, "--release-manifest", WriteReleaseManifest(),
             "--migration", "20261010000100_AssetInitial", "--source", "src/Migrations/20261010000100_AssetInitial.cs", "--compatibility", "safe-expand"],
            stdout, new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Contains("PERSISTENCE_MIGRATION_MISSING", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public async Task RunAsync_ValidateExplainPackVerify_UsesOneCompiledBlueprint()
    {
        CreateFixture();
        var releasePath = WriteReleaseManifest();
        var explainPath = Path.Combine(_root, "out", "explain.json");
        var packagePath = Path.Combine(_root, "out", "Partner.AssetCatalog@1.0.0.npp");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        Assert.Equal(0, await PluginToolApp.RunAsync(
            ["plugin", "validate", "--root", _root, "--release-manifest", releasePath],
            stdout,
            stderr));
        Assert.Equal(0, await PluginToolApp.RunAsync(
            ["plugin", "explain", "--root", _root, "--release-manifest", releasePath, "--output", explainPath],
            stdout,
            stderr));
        Assert.Equal(0, await PluginToolApp.RunAsync(
            ["plugin", "pack", "--root", _root, "--release-manifest", releasePath, "--output", packagePath],
            stdout,
            stderr));
        Assert.Equal(0, await PluginToolApp.RunAsync(
            ["plugin", "verify", "--package", packagePath, "--release-manifest", releasePath],
            stdout,
            stderr));

        Assert.True(File.Exists(explainPath));
        Assert.Contains("/api/plugins/partner-asset-catalog/assets", File.ReadAllText(explainPath), StringComparison.Ordinal);
        Assert.True(File.Exists(packagePath));
        Assert.Contains("\"status\": \"passed\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_ValidateProjectReference_ReturnsStableFailure()
    {
        CreateFixture();
        File.WriteAllText(
            Path.Combine(_root, "Partner.AssetCatalog.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\NewP\server\sdk\Sdk.csproj" />
              </ItemGroup>
            </Project>
            """);
        var stdout = new StringWriter();

        var exitCode = await PluginToolApp.RunAsync(
            ["plugin", "validate", "--root", _root, "--release-manifest", WriteReleaseManifest()],
            stdout,
            new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Contains(PlatformApiDiagnosticCodes.ProjectReferenceForbidden, stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PlatformApiRelease_InspectsActualArchiveIdentitiesAndHashes()
    {
        var contracts = CreateNuGetArtifact("ICP.Platform.Plugin.Contracts", includeSchema: true);
        var backendSdk = CreateNuGetArtifact(
            "ICP.Platform.Plugin.Sdk",
            contractsDependencyVersion: "[1.0.0]");
        var frontendSdk = CreateNpmArtifact();
        var tool = CreateNuGetArtifact("ICP.Platform.Plugin.Tool", duplicateIdentity: true);
        var outputPath = Path.Combine(_root, "feed", "platform-api-release.json");
        var stdout = new StringWriter();

        var exitCode = await PluginToolApp.RunAsync(
            [
                "platform-api", "release",
                "--contracts", contracts,
                "--backend-sdk", backendSdk,
                "--frontend-sdk", frontendSdk,
                "--tool", tool,
                "--source-commit", "commit-actual",
                "--output", outputPath
            ],
            stdout,
            new StringWriter());

        Assert.Equal(0, exitCode);
        var manifest = JsonSerializer.Deserialize<PlatformApiReleaseManifest>(
            File.ReadAllText(outputPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(manifest);
        Assert.Equal("commit-actual", manifest.SourceCommit);
        Assert.Equal(PlatformApiArtifactKinds.Required, manifest.Artifacts.Select(static artifact => artifact.Kind));
        Assert.Equal(
            new[] { "standard-plugin-blueprint.schema.json" },
            manifest.CanonicalSchemas.Keys);
        Assert.StartsWith("sha256:", manifest.CanonicalSchemas["standard-plugin-blueprint.schema.json"], StringComparison.Ordinal);
        Assert.All(manifest.Artifacts, static artifact =>
        {
            Assert.StartsWith("sha256:", artifact.Sha256, StringComparison.Ordinal);
            Assert.Equal(71, artifact.Sha256.Length);
        });
    }

    [Fact]
    public async Task RunAsync_PlatformApiRelease_RejectsNonExactBackendContractsDependency()
    {
        var stdout = new StringWriter();

        var exitCode = await PluginToolApp.RunAsync(
            [
                "platform-api", "release",
                "--contracts", CreateNuGetArtifact("ICP.Platform.Plugin.Contracts", includeSchema: true),
                "--backend-sdk", CreateNuGetArtifact(
                    "ICP.Platform.Plugin.Sdk",
                    contractsDependencyVersion: "1.0.0"),
                "--frontend-sdk", CreateNpmArtifact(),
                "--tool", CreateNuGetArtifact("ICP.Platform.Plugin.Tool", duplicateIdentity: true),
                "--source-commit", "commit-skew",
                "--output", Path.Combine(_root, "feed", "invalid-release.json")
            ],
            stdout,
            new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Contains(PlatformApiDiagnosticCodes.PlatformApiReleaseSkew, stdout.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private void CreateFixture()
    {
        File.WriteAllText(
            Path.Combine(_root, "newp.plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "profile": "standard-data.v1",
              "moduleId": "Partner.AssetCatalog",
              "displayName": "资产目录",
              "version": "1.0.0",
              "compatibility": {
                "platformApi": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" },
                "persistence": { "contract": "ef-migrations.v1", "providers": ["sqlServer"] }
              },
              "standardData": {
                "resources": [{
                  "key": "Asset",
                  "pluralName": "Assets",
                  "displayName": "资产",
                  "displayField": "Name",
                  "fields": [
                    { "name": "Code", "type": "string", "required": true, "unique": true },
                    { "name": "Name", "type": "string", "required": true }
                  ]
                }]
              },
              "navigation": { "slot": "extensions", "group": "扩展示例", "menu": "资产目录", "order": 100 },
              "persistence": {
                "models": ["metadata/models/Asset.model.json"],
                "migrations": { "sqlServer": {
                  "assembly": "migrations/sqlserver/Partner.AssetCatalog.Migrations.dll",
                  "manifest": "migrations/sqlserver/migrations.json"
                }}
              }
            }
            """);
        Directory.CreateDirectory(Path.Combine(_root, "metadata", "models"));
        File.WriteAllText(
            Path.Combine(_root, "metadata", "models", "Asset.model.json"),
            "{ \"schemaVersion\": \"1.0\", \"type\": \"model\", \"Asset\": { \"kind\": \"Entity\", \"fields\": [] } }");
        Directory.CreateDirectory(Path.Combine(_root, "migrations", "sqlserver"));
        File.WriteAllBytes(
            Path.Combine(_root, "migrations", "sqlserver", "Partner.AssetCatalog.Migrations.dll"),
            "MZ-controlled-migration-fixture"u8.ToArray());
        File.WriteAllText(
            Path.Combine(_root, "migrations", "sqlserver", "migrations.json"),
            "{ \"schemaVersion\": \"1.0\", \"moduleId\": \"Partner.AssetCatalog\", \"provider\": \"SqlServer\", \"migrations\": [] }");
    }

    private string WriteReleaseManifest()
    {
        var artifacts = PlatformApiArtifactKinds.Required
            .Select((kind, index) => new PlatformApiArtifactAttestation(
                kind,
                PlatformApiArtifactKinds.PackageIdFor(kind),
                PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest,
                $"feed/artifact-{index}",
                $"sha256:{new string((char)('a' + index), 64)}"))
            .ToArray();
        var manifest = new PlatformApiReleaseManifest(
            "1.0",
            PlatformApiReleaseIdentity.Version,
            PlatformApiReleaseIdentity.ReleaseDigest,
            "commit-123",
            artifacts,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["standard-plugin-blueprint.schema.json"] = $"sha256:{new string('c', 64)}"
            });
        var path = Path.Combine(_root, "platform-api-release.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return path;
    }

    private string CreateNuGetArtifact(
        string packageId,
        bool duplicateIdentity = false,
        bool includeSchema = false,
        string? contractsDependencyVersion = null)
    {
        var path = Path.Combine(_root, "feed", $"{packageId}.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var dependencies = contractsDependencyVersion is null
            ? string.Empty
            : $"<dependencies><group targetFramework=\"net10.0\"><dependency id=\"ICP.Platform.Plugin.Contracts\" version=\"{contractsDependencyVersion}\" /></group></dependencies>";
        WriteZipEntry(
            archive,
            $"{packageId}.nuspec",
            $"<package><metadata><id>{packageId}</id><version>1.0.0</version>{dependencies}</metadata></package>");
        WriteZipEntry(archive, "contentFiles/any/any/platform-api-release.identity.json", ReleaseIdentityJson());
        if (duplicateIdentity)
        {
            WriteZipEntry(archive, "tools/net10.0/any/platform-api-release.identity.json", ReleaseIdentityJson());
        }
        if (includeSchema)
        {
            WriteZipEntry(
                archive,
                "contentFiles/any/any/schemas/standard-plugin-blueprint.schema.json",
                CanonicalSchemaJson());
        }
        return path;
    }

    private string CreateNpmArtifact()
    {
        var path = Path.Combine(_root, "feed", "icp-platform-plugin-sdk-1.0.0.tgz");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new TarWriter(gzip, leaveOpen: false);
        WriteTarEntry(writer, "package/package.json", "{ \"name\": \"@icp-platform/plugin-sdk\", \"version\": \"1.0.0\" }");
        WriteTarEntry(writer, "package/platform-api-release.identity.json", ReleaseIdentityJson());
        WriteTarEntry(writer, "package/schemas/standard-plugin-blueprint.schema.json", CanonicalSchemaJson());
        return path;
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteTarEntry(TarWriter writer, string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
        {
            DataStream = new MemoryStream(bytes)
        });
    }

    private static string ReleaseIdentityJson() =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            version = PlatformApiReleaseIdentity.Version,
            releaseDigest = PlatformApiReleaseIdentity.ReleaseDigest,
            profile = PlatformApiReleaseIdentity.StandardDataProfile,
            persistenceContract = PlatformApiReleaseIdentity.PersistenceContract
        });

    private static string CanonicalSchemaJson() =>
        "{ \"$schema\": \"https://json-schema.org/draft/2020-12/schema\", \"title\": \"fixture\" }";
}
