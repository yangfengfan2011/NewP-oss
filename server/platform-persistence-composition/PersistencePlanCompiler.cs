using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 把模块计划中的声明式模型与 migration bundle 编译成唯一持久化计划。
/// </summary>
public sealed partial class PersistencePlanCompiler : IPersistencePlanCompiler
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly StorageTypeMapDocument _typeMap;
    private readonly JsonModelMetadataDocumentLoader _modelLoader = new();
    private readonly ModelMetadataProjector _projector = new();
    private readonly ModelDefinitionValidator _validator = new();

    public PersistencePlanCompiler(string storageTypeMapPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageTypeMapPath);
        _typeMap = new JsonStorageTypeMapLoader().LoadFromFile(storageTypeMapPath);
    }

    public PersistencePlanCompilationResult Compile(
        CanonicalModulePlan modules,
        PersistenceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var diagnostics = new List<PersistencePlanDiagnostic>();
        var models = new List<ModelDefinition>();
        var streams = new List<ModuleMigrationStream>();

        foreach (var module in modules.Modules.OrderBy(static item => item.ModuleId, StringComparer.Ordinal))
        {
            CompileModule(module, provider, models, streams, diagnostics);
        }

        AddLegacyBaselineStream(modules, provider, models, streams);
        AddGlobalConflictDiagnostics(models, streams, diagnostics);
        var orderedDiagnostics = diagnostics
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ThenBy(static item => item.CanonicalKey, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
        if (orderedDiagnostics.Length > 0)
        {
            return new PersistencePlanCompilationResult(null, orderedDiagnostics);
        }

        var orderedModels = models
            .OrderBy(static model => model.LogicalKey, StringComparer.Ordinal)
            .ToArray();
        var dependencyDepths = BuildDependencyDepths(modules);
        var orderedStreams = streams
            .OrderBy(static stream => stream.IsLegacyBaseline ? 0 : 1)
            .ThenBy(stream => dependencyDepths.GetValueOrDefault(stream.ModuleId, int.MaxValue))
            .ThenBy(static stream => stream.ModuleId, StringComparer.Ordinal)
            .ToArray();
        var canonicalDocument = new
        {
            schemaVersion = "1.0",
            modules.CandidateId,
            modules.SnapshotId,
            modulePlanHash = modules.PlanHash,
            provider = provider.ToString(),
            models = orderedModels,
            streams = orderedStreams.Select(static stream => new
            {
                stream.ModuleId,
                provider = stream.Provider.ToString(),
                stream.BundleChecksum,
                stream.ModelDigest,
                stream.ExpectedFrontier,
                stream.IsLegacyBaseline,
                stream.Migrations
            })
        };
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(canonicalDocument, CanonicalJsonOptions);
        var planDigest = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));

        return new PersistencePlanCompilationResult(
            new CompiledPersistencePlan(
                modules.CandidateId,
                modules.SnapshotId,
                modules.PlanHash,
                provider,
                planDigest,
                canonicalBytes,
                orderedModels,
                orderedStreams),
            Array.Empty<PersistencePlanDiagnostic>());
    }

    private static IReadOnlyDictionary<string, int> BuildDependencyDepths(CanonicalModulePlan modules)
    {
        var byId = modules.Modules.ToDictionary(static module => module.ModuleId, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        int Resolve(string moduleId, HashSet<string> visiting)
        {
            if (depths.TryGetValue(moduleId, out var depth))
            {
                return depth;
            }

            if (!byId.TryGetValue(moduleId, out var module) || !visiting.Add(moduleId))
            {
                return 0;
            }

            depth = module.Dependencies
                .Where(byId.ContainsKey)
                .Select(dependency => Resolve(dependency, visiting) + 1)
                .DefaultIfEmpty(0)
                .Max();
            visiting.Remove(moduleId);
            depths[moduleId] = depth;
            return depth;
        }

        foreach (var moduleId in byId.Keys)
        {
            Resolve(moduleId, new HashSet<string>(StringComparer.Ordinal));
        }

        return depths;
    }

    private void CompileModule(
        CanonicalModulePlanModule module,
        PersistenceProvider provider,
        List<ModelDefinition> models,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var contribution = module.Persistence;
        if (contribution is null || contribution.ModelPaths.Count == 0)
        {
            return;
        }

        if (module.Provenance.SourceKind == "installed-package" && provider is not PersistenceProvider.SqlServer)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_PROVIDER_UNSUPPORTED",
                module.ModuleId,
                provider.ToString(),
                module.Provenance.SourcePath,
                $"Provider '{provider}' does not support external persistence contribution '{module.ModuleId}'."));
            return;
        }

        var moduleModels = new List<ModelDefinition>();
        foreach (var relativePath in contribution.ModelPaths.OrderBy(static path => path, StringComparer.Ordinal))
        {
            var fullPath = ResolveContainedPath(module, relativePath, diagnostics);
            if (fullPath is null)
            {
                continue;
            }

            try
            {
                var document = _modelLoader.LoadFromFile(fullPath);
                if (!string.Equals(document.ModuleId, module.ModuleId, StringComparison.Ordinal))
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_MODEL_OWNER_MISMATCH",
                        module.ModuleId,
                        document.Model.Name,
                        fullPath,
                        $"Model '{document.Model.Name}' declares owner '{document.ModuleId}' instead of '{module.ModuleId}'."));
                    continue;
                }

                if (!module.ModelKeys.Contains(document.Model.Name, StringComparer.Ordinal)
                    && !module.ModelKeys.Contains($"{module.ModuleId}.{document.Model.Name}", StringComparer.Ordinal))
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_MODEL_KEY_UNDECLARED",
                        module.ModuleId,
                        document.Model.Name,
                        fullPath,
                        $"Model '{document.Model.Name}' is absent from the Canonical Module Plan facts for '{module.ModuleId}'."));
                    continue;
                }

                var projected = _projector.Project(document, _typeMap, provider.ToString()) with
                {
                    OwnerModuleId = module.ModuleId,
                    SchemaName = ResolveSchemaName(module)
                };
                _validator.Validate(projected);
                moduleModels.Add(projected);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new PersistencePlanDiagnostic(
                    "PERSISTENCE_MODEL_INVALID",
                    module.ModuleId,
                    relativePath,
                    fullPath,
                    exception.Message));
            }
        }

        models.AddRange(moduleModels);
        if (moduleModels.Count == 0)
        {
            return;
        }

        var modelDigest = PersistenceModelDigest.Compute(moduleModels);
        var matchingBundles = contribution.MigrationBundles
            .Where(bundle => string.Equals(bundle.Provider, provider.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingBundles.Length == 0 && module.Provenance.SourceKind == "built-in")
        {
            // 现有统一 migrations 作为显式 platform.core baseline 承接所有存量内建表；
            // 新外部模块不得复用该退出路径。
            return;
        }

        if (matchingBundles.Length != 1)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_MIGRATION_REQUIRED",
                module.ModuleId,
                provider.ToString(),
                module.Provenance.SourcePath,
                $"Module '{module.ModuleId}' must declare exactly one {provider} migration bundle."));
            return;
        }

        CompileBundle(module, provider, matchingBundles[0], modelDigest, streams, diagnostics);
    }

    private static void AddLegacyBaselineStream(
        CanonicalModulePlan modules,
        PersistenceProvider provider,
        IReadOnlyList<ModelDefinition> models,
        List<ModuleMigrationStream> streams)
    {
        var builtInOwners = modules.Modules
            .Where(static module => module.Provenance.SourceKind == "built-in")
            .Select(static module => module.ModuleId)
            .ToHashSet(StringComparer.Ordinal);
        var moduleOwnedMigrationStreams = streams
            .Where(static stream => !stream.IsLegacyBaseline)
            .Select(static stream => stream.ModuleId)
            .ToHashSet(StringComparer.Ordinal);
        var legacyModels = models
            .Where(model =>
                builtInOwners.Contains(model.OwnerModuleId)
                && !moduleOwnedMigrationStreams.Contains(model.OwnerModuleId))
            .ToArray();
        if (legacyModels.Length == 0)
        {
            return;
        }

        var modelDigest = PersistenceModelDigest.Compute(legacyModels);
        var (assemblyName, frontier) = provider switch
        {
            PersistenceProvider.SqlServer => (
                "PlatformPersistence.EfCore.SqlServerMigrations",
                PlatformCoreMigrationFrontiers.SqlServerLegacyBaseline),
            PersistenceProvider.Oracle => (
                "PlatformPersistence.EfCore.OracleMigrations",
                "20260428145243_AlignModelMetadataEntityKeys"),
            PersistenceProvider.MySql => (
                "PlatformPersistence.EfCore.MySqlMigrations",
                "20260428145227_AlignModelMetadataEntityKeys"),
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };
        streams.Add(new ModuleMigrationStream(
            "platform.core",
            provider,
            assemblyName,
            "platform.core:legacy-baseline",
            modelDigest,
            modelDigest,
            frontier,
            IsLegacyBaseline: true,
            Array.Empty<CompiledMigrationStep>()));
    }

    private void CompileBundle(
        CanonicalModulePlanModule module,
        PersistenceProvider provider,
        ModuleMigrationBundleReference bundle,
        string modelDigest,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var assemblyPath = ResolveContainedPath(module, bundle.AssemblyPath, diagnostics);
        var manifestPath = ResolveContainedPath(module, bundle.ManifestPath, diagnostics);
        if (assemblyPath is null || manifestPath is null)
        {
            return;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<MigrationManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions)
                ?? throw new InvalidOperationException("Migration manifest is empty.");
            ValidateManifest(module, provider, manifest, assemblyPath, manifestPath, modelDigest);

            var migrations = manifest.Migrations
                .OrderBy(static migration => migration.Sequence, StringComparer.Ordinal)
                .Select(static migration => new CompiledMigrationStep(
                    migration.Sequence,
                    migration.MigrationId,
                    migration.PreviousContentDigest,
                    migration.ContentDigest,
                    migration.FromModelDigest,
                    migration.ToModelDigest,
                    migration.Compatibility))
                .ToArray();
            streams.Add(new ModuleMigrationStream(
                module.ModuleId,
                provider,
                assemblyPath,
                manifestPath,
                manifest.BundleChecksum,
                modelDigest,
                migrations[^1].MigrationId,
                IsLegacyBaseline: false,
                migrations));
        }
        catch (PersistencePlanValidationException exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                exception.Code,
                module.ModuleId,
                provider.ToString(),
                manifestPath,
                exception.Message));
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_MIGRATION_MANIFEST_INVALID",
                module.ModuleId,
                provider.ToString(),
                manifestPath,
                exception.Message));
        }
    }

    private static void ValidateManifest(
        CanonicalModulePlanModule module,
        PersistenceProvider provider,
        MigrationManifest manifest,
        string assemblyPath,
        string manifestPath,
        string modelDigest)
    {
        if (!string.Equals(manifest.ModuleId, module.ModuleId, StringComparison.Ordinal)
            || !string.Equals(manifest.Provider, provider.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_MIGRATION_OWNER_MISMATCH",
                $"Migration manifest '{manifestPath}' does not match {module.ModuleId}/{provider}.");
        }

        var checksum = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        if (!string.Equals(checksum, manifest.BundleChecksum, StringComparison.Ordinal))
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_BUNDLE_CHECKSUM_MISMATCH",
                $"Migration bundle checksum mismatch for '{module.ModuleId}'.");
        }

        if (!string.Equals(manifest.ModelDigest, modelDigest, StringComparison.Ordinal))
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_MODEL_MISMATCH",
                $"Migration manifest model digest does not match compiled model '{module.ModuleId}'.");
        }

        var migrations = manifest.Migrations
            .OrderBy(static migration => migration.Sequence, StringComparer.Ordinal)
            .ToArray();
        if (migrations.Length == 0)
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_MIGRATION_REQUIRED",
                $"Migration stream '{module.ModuleId}' is empty.");
        }

        var firstMigration = migrations[0];
        var isTrustedLegacyCutover = LegacyMigrationCutoverPlan.IsTrustedInitialMigration(
            module.ModuleId,
            provider,
            module.Provenance.SourceKind,
            firstMigration.MigrationId,
            firstMigration.Compatibility,
            firstMigration.FromModelDigest);
        var startsFromZeroDigest = string.Equals(
            firstMigration.FromModelDigest,
            new string('0', 64),
            StringComparison.Ordinal);
        var backupRestoreMigrations = migrations
            .Where(static migration => migration.Compatibility == "backup-restore-only")
            .ToArray();
        if ((!startsFromZeroDigest && !isTrustedLegacyCutover)
            || (backupRestoreMigrations.Length > 0
                && (!isTrustedLegacyCutover || backupRestoreMigrations.Length != 1)))
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_MIGRATION_CHAIN_INVALID",
                $"Migration stream '{module.ModuleId}' does not start from its trusted initial model digest.");
        }

        string? previousDigest = null;
        for (var index = 0; index < migrations.Length; index++)
        {
            var migration = migrations[index];
            var expectedSequence = (index + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(migration.Sequence, expectedSequence, StringComparison.Ordinal)
                || !string.Equals(migration.PreviousContentDigest, previousDigest, StringComparison.Ordinal)
                || (index > 0
                    && !string.Equals(
                        migration.FromModelDigest,
                        migrations[index - 1].ToModelDigest,
                        StringComparison.Ordinal))
                || !DigestPattern().IsMatch(migration.ContentDigest)
                || !DigestPattern().IsMatch(migration.FromModelDigest)
                || !DigestPattern().IsMatch(migration.ToModelDigest)
                || (migration.Compatibility is not "safe-expand" and not "backup-restore-only"))
            {
                throw new PersistencePlanValidationException(
                    "PERSISTENCE_MIGRATION_CHAIN_INVALID",
                    $"Migration stream '{module.ModuleId}' is not linear and immutable at sequence '{migration.Sequence}'.");
            }

            previousDigest = migration.ContentDigest;
        }

        if (!string.Equals(migrations[^1].ToModelDigest, modelDigest, StringComparison.Ordinal))
        {
            throw new PersistencePlanValidationException(
                "PERSISTENCE_MODEL_MISMATCH",
                $"Migration frontier does not target the compiled model digest for '{module.ModuleId}'.");
        }
    }

    private static string? ResolveContainedPath(
        CanonicalModulePlanModule module,
        string relativePath,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_ARTIFACT_PATH_INVALID",
                module.ModuleId,
                relativePath,
                module.Provenance.SourcePath,
                $"Persistence artifact path '{relativePath}' must be package-relative."));
            return null;
        }

        var root = Path.GetFullPath(module.PackageRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_ARTIFACT_PATH_INVALID",
                module.ModuleId,
                relativePath,
                fullPath,
                $"Persistence artifact '{relativePath}' is missing or escapes the package root."));
            return null;
        }

        return fullPath;
    }

    private static string ResolveSchemaName(CanonicalModulePlanModule module)
    {
        if (module.Provenance.SourceKind == "built-in")
        {
            return "dbo";
        }

        var normalized = NonSchemaCharacterPattern()
            .Replace(module.ModuleId.ToLowerInvariant(), "_")
            .Trim('_');
        return $"np_{normalized}";
    }

    private static void AddGlobalConflictDiagnostics(
        IReadOnlyList<ModelDefinition> models,
        IReadOnlyList<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        foreach (var group in models.GroupBy(static model => model.LogicalKey, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_MODEL_KEY_CONFLICT",
                group.First().OwnerModuleId,
                group.Key,
                string.Empty,
                $"Logical model key '{group.Key}' has multiple definitions."));
        }

        var modelsByLogicalKey = models.ToLookup(static model => model.LogicalKey, StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            foreach (var foreignKey in model.ForeignKeys)
            {
                var principalModel = modelsByLogicalKey[foreignKey.PrincipalModelKey].FirstOrDefault();
                if (principalModel is null)
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_FOREIGN_KEY_PRINCIPAL_MISSING",
                        model.OwnerModuleId,
                        $"{model.LogicalKey}/{foreignKey.Name}",
                        string.Empty,
                        $"Foreign key '{foreignKey.Name}' references missing principal model '{foreignKey.PrincipalModelKey}'."));
                    continue;
                }

                var principalFieldNames = principalModel.Fields
                    .Select(static field => field.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingPrincipalFields = foreignKey.PrincipalFields
                    .Where(field => !principalFieldNames.Contains(field))
                    .ToArray();
                if (missingPrincipalFields.Length > 0)
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_FOREIGN_KEY_PRINCIPAL_FIELD_MISSING",
                        model.OwnerModuleId,
                        $"{model.LogicalKey}/{foreignKey.Name}",
                        string.Empty,
                        $"Foreign key '{foreignKey.Name}' references undefined principal field(s) '{string.Join(", ", missingPrincipalFields)}'."));
                }
            }
        }

        foreach (var group in models
                     .SelectMany(model => model.ForeignKeys.Select(foreignKey => new { Model = model, ForeignKey = foreignKey }))
                     .GroupBy(
                         static item => $"{item.Model.SchemaName}.{item.ForeignKey.Name}",
                         StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_FOREIGN_KEY_NAME_CONFLICT",
                group.First().Model.OwnerModuleId,
                group.Key,
                string.Empty,
                $"Foreign key name '{group.Key}' is declared more than once in the same schema."));
        }

        foreach (var group in models.GroupBy(
                     static model => $"{model.SchemaName}.{model.TableName}",
                     StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Select(model => model.OwnerModuleId).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_PHYSICAL_OWNER_CONFLICT",
                group.First().OwnerModuleId,
                group.Key,
                string.Empty,
                $"Physical object '{group.Key}' has multiple owners."));
        }

        foreach (var group in streams
                     .SelectMany(stream => stream.Migrations.Select(migration => new { Stream = stream, Migration = migration }))
                     .GroupBy(static item => item.Migration.MigrationId, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Select(item => item.Stream.ModuleId).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_MIGRATION_ID_CONFLICT",
                group.First().Stream.ModuleId,
                group.Key,
                group.First().Stream.ManifestPath,
                $"Migration id '{group.Key}' is owned by multiple module streams."));
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSchemaCharacterPattern();

    private sealed record MigrationManifest(
        string SchemaVersion,
        string Provider,
        string ModuleId,
        string ModelDigest,
        string BundleChecksum,
        IReadOnlyList<MigrationManifestStep> Migrations);

    private sealed record MigrationManifestStep(
        string Sequence,
        string MigrationId,
        string? PreviousContentDigest,
        string ContentDigest,
        string FromModelDigest,
        string ToModelDigest,
        string Compatibility);

    private sealed class PersistencePlanValidationException : Exception
    {
        public PersistencePlanValidationException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
