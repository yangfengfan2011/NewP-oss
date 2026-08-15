using System.IO.Compression;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

namespace ICP.Platform.PluginApi.Tests;

public sealed class StandardPluginPackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("newp_package_").FullName;

    [Fact]
    public void PackAndVerify_StandardDataPackage_IsDeterministicAndContainsNoRuntimeSdk()
    {
        CreateAuthoringFixture();
        var firstPath = Path.Combine(_root, "out", "Partner.AssetCatalog@1.0.0.npp");
        var secondPath = Path.Combine(_root, "out", "Partner.AssetCatalog@1.0.0-repack.npp");
        var release = ReleaseManifest();
        var builder = new StandardPluginPackageBuilder();

        var first = builder.Pack(new StandardPluginPackRequest(_root, firstPath, release));
        var second = builder.Pack(new StandardPluginPackRequest(_root, secondPath, release));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.PackageSha256, second.PackageSha256);
        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

        using var archive = ZipFile.OpenRead(firstPath);
        var entries = archive.Entries.Select(static entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
        Assert.Contains("plugin.json", entries);
        Assert.Contains("authoring/newp.plugin.json", entries);
        Assert.Contains("attestation/platform-api.json", entries);
        Assert.Contains("projections/standard-data.json", entries);
        Assert.Contains("provenance/explain.json", entries);
        Assert.Contains("checksums/sha256sums.json", entries);
        Assert.Contains("metadata/views/AssetDetail.view.json", entries);
        Assert.Contains("migrations/sqlserver/Partner.AssetCatalog.Migrations.dll", entries);
        Assert.DoesNotContain(entries, static entry => entry.StartsWith("backend/", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, static entry => entry.StartsWith("frontend/", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, static entry => entry.EndsWith("ICP.Platform.Plugin.Contracts.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, static entry => entry.EndsWith("ICP.Platform.Plugin.Sdk.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, static entry => entry.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

        var verified = new StandardPluginPackageVerifier().Verify(
            new StandardPluginVerifyRequest(firstPath, release));
        Assert.True(
            verified.IsValid,
            string.Join(Environment.NewLine, verified.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Empty(verified.Diagnostics);
        Assert.Equal("Partner.AssetCatalog", verified.Manifest?.ModuleId);
        using var detailEntry = Assert.Single(archive.Entries, static entry =>
            entry.FullName == "metadata/views/AssetDetail.view.json").Open();
        using var detailReader = new StreamReader(detailEntry);
        Assert.Contains("partner.asset.detail", detailReader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_FrontendRuntimeArtifact_IsRejected()
    {
        CreateAuthoringFixture();
        var frontend = Path.Combine(_root, "frontend");
        Directory.CreateDirectory(frontend);
        File.WriteAllText(Path.Combine(frontend, "index.js"), "export default {};\n");

        var result = new StandardPluginPackageBuilder().Pack(
            new StandardPluginPackRequest(
                _root,
                Path.Combine(_root, "out", "forbidden.npp"),
                ReleaseManifest()));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            PlatformApiDiagnosticCodes.StandardProfileFrontendCodeForbidden,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Verify_ReleaseDigestChangedAfterPack_ReturnsAttestationMismatch()
    {
        CreateAuthoringFixture();
        var packagePath = Path.Combine(_root, "out", "Partner.AssetCatalog@1.0.0.npp");
        var release = ReleaseManifest();
        Assert.True(new StandardPluginPackageBuilder().Pack(
            new StandardPluginPackRequest(_root, packagePath, release)).IsSuccess);
        var tamperedRelease = release with { ReleaseDigest = $"sha256:{new string('f', 64)}" };

        var result = new StandardPluginPackageVerifier().Verify(
            new StandardPluginVerifyRequest(packagePath, tamperedRelease));

        Assert.False(result.IsValid);
        Assert.Equal(
            PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Verify_ReleaseManifestMissingCanonicalSchema_ReturnsReleaseSkew()
    {
        CreateAuthoringFixture();
        var packagePath = Path.Combine(_root, "out", "Partner.AssetCatalog@1.0.0.npp");
        var release = ReleaseManifest();
        Assert.True(new StandardPluginPackageBuilder().Pack(
            new StandardPluginPackRequest(_root, packagePath, release)).IsSuccess);
        var incompleteRelease = release with
        {
            CanonicalSchemas = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var result = new StandardPluginPackageVerifier().Verify(
            new StandardPluginVerifyRequest(packagePath, incompleteRelease));

        Assert.False(result.IsValid);
        Assert.Equal(PlatformApiDiagnosticCodes.PlatformApiReleaseSkew, Assert.Single(result.Diagnostics).Code);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private void CreateAuthoringFixture()
    {
        File.WriteAllText(Path.Combine(_root, "newp.plugin.json"), BlueprintJson());
        var models = Path.Combine(_root, "metadata", "models");
        Directory.CreateDirectory(models);
        File.WriteAllText(
            Path.Combine(models, "Asset.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Partner.AssetCatalog.Asset": {
                "displayField": "Name"
              }
            }
            """);
        var views = Path.Combine(_root, "metadata", "views");
        Directory.CreateDirectory(views);
        File.WriteAllText(
            Path.Combine(views, "AssetDetail.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": {
                "key": "partner.asset.detail",
                "mode": "detail",
                "model": "Asset",
                "sections": [
                  { "key": "basic", "titleKey": "Basic", "fields": ["Name", "PurchasedAt"] }
                ]
              }
            }
            """);
        var migrations = Path.Combine(_root, "migrations", "sqlserver");
        Directory.CreateDirectory(migrations);
        File.WriteAllBytes(
            Path.Combine(migrations, "Partner.AssetCatalog.Migrations.dll"),
            "MZ-controlled-migration-fixture"u8.ToArray());
        File.WriteAllText(
            Path.Combine(migrations, "migrations.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Partner.AssetCatalog",
              "provider": "SqlServer",
              "migrations": []
            }
            """);
    }

    private static PlatformApiReleaseManifest ReleaseManifest()
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
        return new PlatformApiReleaseManifest(
            "1.0",
            PlatformApiReleaseIdentity.Version,
            PlatformApiReleaseIdentity.ReleaseDigest,
            "commit-123",
            artifacts,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["standard-plugin-blueprint.schema.json"] = $"sha256:{new string('c', 64)}"
            });
    }

    private static string BlueprintJson() =>
        """
        {
          "schemaVersion": "1.0",
          "profile": "standard-data.v1",
          "moduleId": "Partner.AssetCatalog",
          "displayName": "资产目录",
          "version": "1.0.0",
          "description": "Partner-owned asset catalog",
          "compatibility": {
            "platformApi": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" },
            "persistence": { "contract": "ef-migrations.v1", "providers": ["sqlServer"] }
          },
          "standardData": {
            "resources": [
              {
                "key": "Asset",
                "pluralName": "Assets",
                "displayName": "资产",
                "displayField": "Name",
                "detailViewKey": "partner.asset.detail",
                "fields": [
                  { "name": "Code", "type": "string", "required": true, "unique": true },
                  { "name": "Name", "type": "string", "required": true },
                  { "name": "PurchasedAt", "type": "date", "required": false }
                ]
              }
            ]
          },
          "navigation": { "slot": "extensions", "group": "扩展示例", "menu": "资产目录", "order": 100 },
          "persistence": {
            "models": ["metadata/models/Asset.model.json"],
            "migrations": {
              "sqlServer": {
                "assembly": "migrations/sqlserver/Partner.AssetCatalog.Migrations.dll",
                "manifest": "migrations/sqlserver/migrations.json"
              }
            }
          }
        }
        """;
}
