using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class StandardDataPackageTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "newp-standard-data-package-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Inspect_ValidPackage_ReturnsManifestBlueprintAndProjection()
    {
        var packageRoot = WritePackage("1.0.0");
        var inspector = new StandardDataPackageInspector(CreateHostCatalog());

        var result = inspector.Inspect(packageRoot);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal("Partner.AssetCatalog", result.Manifest!.ModuleId);
        Assert.Equal("Asset", Assert.Single(result.Blueprint!.StandardData.Resources).Key);
        Assert.Equal("/assets", Assert.Single(result.Projection!.Resources).PageRoute);
    }

    [Fact]
    public void Inspect_WhenPackageContentIsTampered_FailsBeforeActivation()
    {
        var packageRoot = WritePackage("1.0.0");
        File.AppendAllText(
            Path.Combine(packageRoot, "metadata", "models", "Asset.model.json"),
            "tampered");
        var inspector = new StandardDataPackageInspector(CreateHostCatalog());

        var result = inspector.Inspect(packageRoot);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Inspect_WhenReleaseDigestIsUnknown_FailsCompatibilityPreflight()
    {
        var packageRoot = WritePackage("1.0.0");
        var inspector = new StandardDataPackageInspector(
            new PlatformApiHostCatalog(
                PlatformApiReleaseIdentity.Version,
                [PlatformApiReleaseIdentity.StandardDataProfile],
                [new PlatformApiReleaseReference(
                    PlatformApiReleaseIdentity.Version,
                    "sha256:" + new string('f', 64))]));

        var result = inspector.Inspect(packageRoot);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Compile_DesiredVersion_ProducesStandardDataCandidateWithoutRuntimeCode()
    {
        var v1Root = WritePackage("1.0.0");
        var v11Root = WritePackage("1.1.0");
        var inventory = new[]
        {
            new InstalledPluginInventoryItem(
                "Partner.AssetCatalog",
                ["1.0.0", "1.1.0"],
                "1.0.0",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["1.0.0"] = v1Root,
                    ["1.1.0"] = v11Root
                })
        };
        var state = new ResolvedPluginLifecycleState(
            [new PluginDesiredModuleState("Partner.AssetCatalog", "1.1.0", Enabled: true)],
            null,
            null,
            UsesLegacyFallback: false);
        var compiler = new StandardDataCandidatePlanCompiler(
            builtInModulesRoot: null,
            new StandardDataPackageInspector(CreateHostCatalog()));

        var plan = compiler.Compile(inventory, state);

        var module = Assert.Single(plan.Modules);
        Assert.Equal("1.1.0", module.Version);
        Assert.Null(module.BackendEntry);
        Assert.Equal("projections/standard-data.json", module.FrontendEntry);
        Assert.Equal("standard-data", module.FrontendBindingMode);
        Assert.Contains("/api/standard/partner-asset-catalog/assets", module.Routes);
        Assert.Contains("/assets", module.Routes);
        Assert.Contains("Partner.AssetCatalog.Asset.View", module.PermissionCodes);
        Assert.Equal("Asset", Assert.Single(module.ModelKeys));
        Assert.Equal("installed-package", module.Provenance.SourceKind);
    }

    [Fact]
    public void Install_ValidPackage_RecordsVersionWithoutChangingLegacyEnabledState()
    {
        var installRoot = Path.Combine(_root, "install-root");
        var packageRoot = WritePackage("1.1.0");
        var stateStore = new InstalledPluginStateStore(installRoot);
        Assert.True(stateStore.TrySave(new InstalledPluginRecord
        {
            ModuleId = "Partner.AssetCatalog",
            Version = "1.0.0",
            Status = InstalledPluginLifecycleState.Installed,
            PackageRoot = Path.Combine(installRoot, "packages", "Partner.AssetCatalog", "1.0.0"),
            CurrentEnabledVersion = "1.0.0",
            IsDisabled = false,
            InstalledVersions = ["1.0.0"]
        }, out var seedError), seedError);
        var installer = new StandardDataPackageInstaller(
            installRoot,
            stateStore,
            new StandardDataPackageInspector(CreateHostCatalog()));

        var result = installer.Install(packageRoot);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(stateStore.TryLoad("Partner.AssetCatalog", out var record, out var loadError), loadError);
        Assert.NotNull(record);
        Assert.Equal("1.0.0", record!.CurrentEnabledVersion);
        Assert.False(record.IsDisabled);
        Assert.Equal(["1.0.0", "1.1.0"], record.InstalledVersions);
        Assert.True(File.Exists(Path.Combine(
            installRoot,
            "packages",
            "Partner.AssetCatalog",
            "1.1.0",
            "plugin.json")));
        Assert.False(File.Exists(Path.Combine(
            installRoot,
            "packages",
            "Partner.AssetCatalog",
            "1.1.0",
            "package.zip")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WritePackage(string version)
    {
        var packageRoot = Path.Combine(_root, version.Replace('.', '-'));
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["authoring/newp.plugin.json"] = JsonSerializer.SerializeToUtf8Bytes(Blueprint(version), JsonOptions),
            ["attestation/platform-api.json"] = JsonSerializer.SerializeToUtf8Bytes(Attestation(), JsonOptions),
            ["projections/standard-data.json"] = JsonSerializer.SerializeToUtf8Bytes(Projection(), JsonOptions),
            ["provenance/explain.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1.0\"}"),
            ["metadata/models/Asset.model.json"] = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":\"1.0\",\"type\":\"model\",\"Asset\":{\"kind\":\"Entity\",\"fields\":[]}}"),
            ["migrations/sqlserver/Partner.AssetCatalog.Migrations.dll"] = [1, 2, 3],
            ["migrations/sqlserver/migrations.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1.0\"}")
        };
        var manifest = new StandardPluginPackageManifest(
            1,
            PlatformApiReleaseIdentity.StandardDataProfile,
            "Partner.AssetCatalog",
            "Asset Catalog",
            version,
            null,
            Attestation(),
            new StandardPluginPackageArtifacts(
                "authoring/newp.plugin.json",
                "projections/standard-data.json",
                "provenance/explain.json"),
            Blueprint(version).Persistence,
            new StandardPluginPackageChecksums("checksums/sha256sums.json"));
        files["plugin.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var checksums = new StandardPluginChecksumManifest(
            "1.0",
            files.ToDictionary(
                static item => item.Key,
                static item => Sha256(item.Value),
                StringComparer.Ordinal));
        files["checksums/sha256sums.json"] = JsonSerializer.SerializeToUtf8Bytes(checksums, JsonOptions);

        foreach (var file in files)
        {
            var path = Path.Combine(packageRoot, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Value);
        }

        return packageRoot;
    }

    private static StandardPluginBlueprint Blueprint(string version) =>
        new(
            "1.0",
            PlatformApiReleaseIdentity.StandardDataProfile,
            "Partner.AssetCatalog",
            "Asset Catalog",
            version,
            null,
            new StandardPluginCompatibility(
                new PlatformApiVersionRange("1.0.0", "2.0.0"),
                new StandardPersistenceCompatibility(
                    PlatformApiReleaseIdentity.PersistenceContract,
                    [PlatformApiReleaseIdentity.SqlServerProvider])),
            new StandardDataBlueprint(
                [
                    new StandardDataResourceBlueprint(
                        "Asset",
                        "Assets",
                        "Asset",
                        "Name",
                        [new StandardDataFieldBlueprint("Name", "string", Required: true)])
                ]),
            new StandardNavigationBlueprint("business", "Assets", "Assets", 200),
            new StandardPersistenceBlueprint(
                ["metadata/models/Asset.model.json"],
                new Dictionary<string, StandardMigrationBundleBlueprint>(StringComparer.Ordinal)
                {
                    [PlatformApiReleaseIdentity.SqlServerProvider] = new(
                        "migrations/sqlserver/Partner.AssetCatalog.Migrations.dll",
                        "migrations/sqlserver/migrations.json")
                }));

    private static StandardPluginProjection Projection() =>
        new(
            "partner-asset-catalog",
            [
                new StandardDataResourceProjection(
                    "Asset",
                    "/api/standard/partner-asset-catalog/assets",
                    "/assets",
                    "np_partner_assetcatalog",
                    "assets",
                    ["Name"],
                    [
                        "Partner.AssetCatalog.Asset.View",
                        "Partner.AssetCatalog.Asset.Create",
                        "Partner.AssetCatalog.Asset.Update",
                        "Partner.AssetCatalog.Asset.Delete"
                    ])
            ]);

    private static PlatformApiAttestation Attestation() =>
        new(
            PlatformApiReleaseIdentity.Version,
            "1.0.0",
            "2.0.0",
            PlatformApiReleaseIdentity.ReleaseDigest,
            PlatformApiReleaseIdentity.StandardDataProfile,
            PlatformApiReleaseIdentity.PersistenceContract,
            [PlatformApiReleaseIdentity.SqlServerProvider]);

    private static PlatformApiHostCatalog CreateHostCatalog() =>
        new(
            PlatformApiReleaseIdentity.Version,
            [PlatformApiReleaseIdentity.StandardDataProfile],
            [new PlatformApiReleaseReference(
                PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest)]);

    private static string Sha256(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}
