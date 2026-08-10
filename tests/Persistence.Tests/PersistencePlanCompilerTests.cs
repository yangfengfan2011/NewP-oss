using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistencePlanCompilerTests
{
    [Fact]
    public void Compile_ExternalOwnedModel_UsesQualifiedKeySchemaAndDeterministicDigest()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        var module = package.BuildModule();
        var canonicalPlan = BuildCanonicalPlan(module);
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var first = compiler.Compile(canonicalPlan, PersistenceProvider.SqlServer);
        var second = compiler.Compile(BuildCanonicalPlan(module), PersistenceProvider.SqlServer);

        Assert.True(first.IsSuccess, string.Join(Environment.NewLine, first.Diagnostics.Select(static item => item.Message)));
        Assert.True(second.IsSuccess);
        Assert.NotNull(first.Plan);
        Assert.NotNull(second.Plan);
        Assert.Equal(first.Plan.PlanDigest, second.Plan.PlanDigest);
        Assert.Equal(first.Plan.CanonicalBytes, second.Plan.CanonicalBytes);
        Assert.Equal("Partner.AssetCatalog/Asset", first.Plan.Models.Single().LogicalKey);
        Assert.Equal("Partner.AssetCatalog", first.Plan.Models.Single().OwnerModuleId);
        Assert.Equal("np_partner_assetcatalog", first.Plan.Models.Single().SchemaName);
        Assert.Same(first.Plan, first.Plan.RuntimeModelPlan);
        Assert.Same(first.Plan, first.Plan.MigrationPlan);
    }

    [Fact]
    public void Compile_InstalledPackageModelAtCanonicalLayout_UsesPackageOwner()
    {
        using var package = TestPackage.CreateInstalled("Partner.AssetCatalog", "Asset");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(
            BuildCanonicalPlan(package.BuildModule()),
            PersistenceProvider.SqlServer);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.Equal("Partner.AssetCatalog", Assert.Single(result.Plan!.Models).OwnerModuleId);
    }

    [Fact]
    public void Compile_ModelIsAbsentFromCanonicalModuleFacts_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        var module = package.BuildModule(["OtherModel"]);
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(module), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MODEL_KEY_UNDECLARED"
            && item.ModuleId == "Partner.AssetCatalog"
            && item.CanonicalKey == "Asset");
    }

    [Fact]
    public void Compile_ForeignKeyPrincipalModelIsMissing_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteModel(model =>
            model["foreignKeys"] = JsonNode.Parse("""
                [
                  {
                    "name": "FK_Asset_Missing",
                    "fields": ["Code"],
                    "principalModuleId": "Partner.Missing",
                    "principalModel": "Missing",
                    "principalFields": ["EID"],
                    "onDelete": "Restrict"
                  }
                ]
                """));
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_FOREIGN_KEY_PRINCIPAL_MISSING"
            && item.ModuleId == "Partner.AssetCatalog"
            && item.CanonicalKey == "Partner.AssetCatalog/Asset/FK_Asset_Missing");
    }

    [Fact]
    public void Compile_ForeignKeyPrincipalFieldIsMissing_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteModel(model =>
            model["foreignKeys"] = JsonNode.Parse("""
                [
                  {
                    "name": "FK_Asset_Self",
                    "fields": ["Code"],
                    "principalModuleId": "Partner.AssetCatalog",
                    "principalModel": "Asset",
                    "principalFields": ["MissingEID"],
                    "onDelete": "Restrict"
                  }
                ]
                """));
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_FOREIGN_KEY_PRINCIPAL_FIELD_MISSING"
            && item.ModuleId == "Partner.AssetCatalog"
            && item.CanonicalKey == "Partner.AssetCatalog/Asset/FK_Asset_Self");
    }

    [Fact]
    public void Compile_ForeignKeyNamesConflictInSchema_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteModel(model =>
            model["foreignKeys"] = JsonNode.Parse("""
                [
                  {
                    "name": "FK_Asset_Self",
                    "fields": ["Code"],
                    "principalModuleId": "Partner.AssetCatalog",
                    "principalModel": "Asset",
                    "principalFields": ["EID"],
                    "onDelete": "Restrict"
                  },
                  {
                    "name": "FK_Asset_Self",
                    "fields": ["ENAME"],
                    "principalModuleId": "Partner.AssetCatalog",
                    "principalModel": "Asset",
                    "principalFields": ["EID"],
                    "onDelete": "Restrict"
                  }
                ]
                """));
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_FOREIGN_KEY_NAME_CONFLICT"
            && item.ModuleId == "Partner.AssetCatalog"
            && item.CanonicalKey == "np_partner_assetcatalog.FK_Asset_Self");
    }

    [Fact]
    public void Compile_ModuleStreamsShareMigrationId_RejectsWholeCandidate()
    {
        using var firstPackage = TestPackage.Create("Partner.AssetCatalog", "Asset");
        using var secondPackage = TestPackage.Create("Partner.SecondCatalog", "Asset");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(
            BuildCanonicalPlan(firstPackage.BuildModule(), secondPackage.BuildModule()),
            PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MIGRATION_ID_CONFLICT"
            && item.CanonicalKey == "20260809000000_partner_assetcatalog_000001_initial_11111111");
    }

    [Fact]
    public void Compile_MigrationSequenceGap_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteManifest(manifest =>
            manifest["migrations"]!.AsArray()[0]!["sequence"] = "000002");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MIGRATION_CHAIN_INVALID"
            && item.ModuleId == "Partner.AssetCatalog");
    }

    [Fact]
    public void Compile_MigrationFork_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteManifest(manifest =>
        {
            var migrations = manifest["migrations"]!.AsArray();
            var fork = migrations[0]!.DeepClone();
            fork["migrationId"] = "20260809000001_partner_assetcatalog_fork_22222222";
            fork["contentDigest"] = new string('2', 64);
            migrations.Add(fork);
        });
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MIGRATION_CHAIN_INVALID");
    }

    [Fact]
    public void Compile_InstalledModuleBackupRestoreOnlyMigration_IsRejected()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteManifest(manifest =>
            manifest["migrations"]!.AsArray()[0]!["compatibility"] = "backup-restore-only");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, static item => item.Code == "PERSISTENCE_MIGRATION_CHAIN_INVALID");
    }

    [Fact]
    public void Compile_MigrationModelChainBreak_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteManifest(manifest =>
        {
            var migrations = manifest["migrations"]!.AsArray();
            var first = migrations[0]!;
            var finalModelDigest = manifest["modelDigest"]!.GetValue<string>();
            first["toModelDigest"] = new string('e', 64);
            migrations.Add(new JsonObject
            {
                ["sequence"] = "000002",
                ["migrationId"] = "20260809000001_partner_assetcatalog_000002_index_22222222",
                ["previousContentDigest"] = first["contentDigest"]!.GetValue<string>(),
                ["contentDigest"] = new string('2', 64),
                ["fromModelDigest"] = new string('d', 64),
                ["toModelDigest"] = finalModelDigest,
                ["compatibility"] = "safe-expand"
            });
        });
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MIGRATION_CHAIN_INVALID");
    }

    [Fact]
    public void Compile_MigrationFrontierTargetsDifferentModel_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        package.RewriteManifest(manifest =>
            manifest["migrations"]!.AsArray()[0]!["toModelDigest"] = new string('f', 64));
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_MODEL_MISMATCH"
            && item.ModuleId == "Partner.AssetCatalog");
    }

    [Fact]
    public void Compile_BundleChecksumChangedAfterManifest_RejectsWholeCandidate()
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        using (var stream = new FileStream(package.BundlePath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            stream.WriteByte(0);
        }
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), PersistenceProvider.SqlServer);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == "PERSISTENCE_BUNDLE_CHECKSUM_MISMATCH"
            && item.ModuleId == "Partner.AssetCatalog");
    }

    [Fact]
    public void Compile_BuiltInModuleOwnedStreams_ExcludeModelsFromLegacyAndFollowDependencies()
    {
        using var identity = TestPackage.Create("Identity", "User");
        using var authentication = TestPackage.Create("Authentication", "AuthenticationSession");
        authentication.RewriteManifest(manifest =>
            manifest["migrations"]!.AsArray()[0]!["migrationId"] =
                "20260810000001_authentication_000001_foundation_22222222");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(
            BuildCanonicalPlan(
                identity.BuildBuiltInModule(),
                authentication.BuildBuiltInModule(["Identity"])),
            PersistenceProvider.SqlServer);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        Assert.Equal(
            ["Identity", "Authentication"],
            result.Plan!.Streams.Select(static stream => stream.ModuleId));
        Assert.DoesNotContain(result.Plan.Streams, static stream => stream.IsLegacyBaseline);
    }

    [Theory]
    [InlineData(PersistenceProvider.Oracle)]
    [InlineData(PersistenceProvider.MySql)]
    public void Compile_ExternalPersistenceForUnsupportedProvider_RejectsBeforeRuntime(
        PersistenceProvider provider)
    {
        using var package = TestPackage.Create("Partner.AssetCatalog", "Asset");
        var compiler = new PersistencePlanCompiler(FindStorageTypeMap());

        var result = compiler.Compile(BuildCanonicalPlan(package.BuildModule()), provider);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PERSISTENCE_PROVIDER_UNSUPPORTED"
            && item.ModuleId == "Partner.AssetCatalog"
            && item.CanonicalKey == provider.ToString());
    }

    private static CanonicalModulePlan BuildCanonicalPlan(params CanonicalModulePlanModule[] modules) =>
        new(
            CandidateId: "candidate-s1",
            CompilerContractVersion: "1.0",
            PlanHash: new string('a', 64),
            SnapshotId: new string('a', 64),
            CanonicalBytes: Encoding.UTF8.GetBytes("candidate-s1"),
            Modules: modules);

    private static string FindStorageTypeMap()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "server", "platform-modeling", "metadata", "storage-types.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位 storage-types.json。");
    }

    private sealed class TestPackage : IDisposable
    {
        private readonly string _moduleId;
        private readonly string _modelKey;
        private readonly string _cleanupRoot;
        private readonly string _modelRelativePath;

        private TestPackage(
            string root,
            string cleanupRoot,
            string moduleId,
            string modelKey,
            string modelRelativePath)
        {
            Root = root;
            _cleanupRoot = cleanupRoot;
            _moduleId = moduleId;
            _modelKey = modelKey;
            _modelRelativePath = modelRelativePath;
        }

        public string Root { get; }

        public string BundlePath => Path.Combine(
            Root,
            "backend",
            "Partner.AssetCatalog.SqlServerMigrations.dll");

        private string ManifestPath => Path.Combine(
            Root,
            "backend",
            "Partner.AssetCatalog.SqlServerMigrations.json");

        public static TestPackage Create(string moduleId, string modelKey) =>
            Create(moduleId, modelKey, installedLayout: false);

        public static TestPackage CreateInstalled(string moduleId, string modelKey) =>
            Create(moduleId, modelKey, installedLayout: true);

        private static TestPackage Create(string moduleId, string modelKey, bool installedLayout)
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "newp-b9-tests", Guid.NewGuid().ToString("N"));
            var root = installedLayout
                ? Path.Combine(cleanupRoot, "packages", moduleId, "1.0.0")
                : cleanupRoot;
            var modelRelativePath = installedLayout
                ? Path.Combine("metadata", "models", $"{modelKey}.model.json")
                : Path.Combine("modules", moduleId, "metadata", "models", $"{modelKey}.model.json");
            var metadataRoot = Path.GetDirectoryName(Path.Combine(root, modelRelativePath))
                ?? throw new InvalidOperationException("测试模型路径缺少父目录。");
            var backendRoot = Path.Combine(root, "backend");
            Directory.CreateDirectory(metadataRoot);
            Directory.CreateDirectory(backendRoot);

            var modelPath = Path.Combine(metadataRoot, $"{modelKey}.model.json");
            var modelJson = """
                {
                  "schemaVersion": "1.0",
                  "type": "model",
                  "Asset": {
                    "kind": "Entity",
                    "builtinFields": ["ENAME"],
                    "fields": [
                      {
                        "name": "Code",
                        "storage": { "type": "string" }
                      }
                    ]
                  }
                }
                """.Replace("\"Asset\":", $"\"{modelKey}\":", StringComparison.Ordinal);
            File.WriteAllText(modelPath, modelJson);

            var bundlePath = Path.Combine(backendRoot, "Partner.AssetCatalog.SqlServerMigrations.dll");
            File.Copy(typeof(PersistencePlanCompilerTests).Assembly.Location, bundlePath);

            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath);
            var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(FindStorageTypeMap());
            var projected = new ModelMetadataProjector().Project(document, typeMap, "SqlServer") with
            {
                OwnerModuleId = moduleId,
                SchemaName = $"np_{moduleId.ToLowerInvariant().Replace('.', '_')}"
            };
            var modelDigest = PersistenceModelDigest.Compute([projected]);
            var bundleChecksum = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(bundlePath)));
            var migrationId = "20260809000000_partner_assetcatalog_000001_initial_11111111";
            var migrationManifest = new
            {
                schemaVersion = "1.0",
                provider = "SqlServer",
                moduleId,
                modelDigest,
                bundleChecksum,
                migrations = new[]
                {
                    new
                    {
                        sequence = "000001",
                        migrationId,
                        previousContentDigest = (string?)null,
                        contentDigest = new string('1', 64),
                        fromModelDigest = new string('0', 64),
                        toModelDigest = modelDigest,
                        compatibility = "safe-expand"
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(backendRoot, "Partner.AssetCatalog.SqlServerMigrations.json"),
                JsonSerializer.Serialize(migrationManifest));

            return new TestPackage(root, cleanupRoot, moduleId, modelKey, modelRelativePath);
        }

        public CanonicalModulePlanModule BuildModule(IReadOnlyList<string>? modelKeys = null) =>
            new(
                ModuleId: _moduleId,
                Version: "1.0.0",
                DisplayName: "Asset Catalog",
                Description: null,
                PackageRoot: Root,
                BackendEntry: null,
                BackendBehaviorKey: null,
                FrontendEntry: null,
                FrontendBehaviorKey: null,
                FrontendBindingMode: "behavior-bindings",
                Dependencies: [],
                Routes: [],
                FrontendBindings: [],
                FrontendBindingReferences: [],
                PermissionCodes: [],
                PermissionReferences: [],
                ModelKeys: modelKeys ?? [_modelKey],
                Provenance: new ModulePlanProvenance("installed-package", Path.Combine(Root, "plugin.json"), "/"),
                Persistence: new ModulePersistenceContribution(
                    [_modelRelativePath],
                    [new ModuleMigrationBundleReference(
                        "SqlServer",
                        Path.Combine("backend", "Partner.AssetCatalog.SqlServerMigrations.dll"),
                        Path.Combine("backend", "Partner.AssetCatalog.SqlServerMigrations.json"))]));

        public CanonicalModulePlanModule BuildBuiltInModule(IReadOnlyList<string>? dependencies = null)
        {
            var modelPath = Path.Combine(Root, _modelRelativePath);
            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath);
            var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(FindStorageTypeMap());
            var projected = new ModelMetadataProjector().Project(document, typeMap, "SqlServer") with
            {
                OwnerModuleId = _moduleId,
                SchemaName = "dbo"
            };
            var modelDigest = PersistenceModelDigest.Compute([projected]);
            RewriteManifest(manifest =>
            {
                manifest["modelDigest"] = modelDigest;
                manifest["migrations"]!.AsArray()[0]!["toModelDigest"] = modelDigest;
            });

            var module = BuildModule();
            return module with
            {
                Dependencies = dependencies ?? [],
                Provenance = new ModulePlanProvenance("built-in", Path.Combine(Root, "plugin.json"), "/")
            };
        }

        public void RewriteManifest(Action<JsonObject> rewrite)
        {
            ArgumentNullException.ThrowIfNull(rewrite);
            var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))?.AsObject()
                ?? throw new InvalidOperationException("测试 migration manifest 无法读取。");
            rewrite(manifest);
            File.WriteAllText(ManifestPath, manifest.ToJsonString());
        }

        public void RewriteModel(Action<JsonObject> rewrite)
        {
            ArgumentNullException.ThrowIfNull(rewrite);
            var modelPath = Path.Combine(Root, _modelRelativePath);
            var document = JsonNode.Parse(File.ReadAllText(modelPath))?.AsObject()
                ?? throw new InvalidOperationException("测试模型 metadata 无法读取。");
            rewrite(document[_modelKey]?.AsObject()
                ?? throw new InvalidOperationException("测试模型 metadata 缺少目标模型。"));
            File.WriteAllText(modelPath, document.ToJsonString());

            var metadata = new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath);
            var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(FindStorageTypeMap());
            var projected = new ModelMetadataProjector().Project(metadata, typeMap, "SqlServer") with
            {
                OwnerModuleId = _moduleId,
                SchemaName = $"np_{_moduleId.ToLowerInvariant().Replace('.', '_')}"
            };
            var modelDigest = PersistenceModelDigest.Compute([projected]);
            RewriteManifest(manifest =>
            {
                manifest["modelDigest"] = modelDigest;
                manifest["migrations"]!.AsArray()[0]!["toModelDigest"] = modelDigest;
            });
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupRoot))
            {
                Directory.Delete(_cleanupRoot, recursive: true);
            }
        }
    }
}
