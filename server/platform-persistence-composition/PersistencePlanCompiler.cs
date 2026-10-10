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
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly StorageTypeMapDocument _typeMap;
    private readonly string _platformFoundationRoot;
    private readonly JsonModelMetadataDocumentLoader _modelLoader = new();
    private readonly ModelMetadataProjector _projector = new();
    private readonly ModelDefinitionValidator _validator = new();

    public PersistencePlanCompiler(string storageTypeMapPath)
        : this(storageTypeMapPath, AppContext.BaseDirectory)
    {
    }

    // 只读 preflight 可把正式编译器的平台 bundle 根固定到 Host Release 输出；
    // runtime 继续使用 AppContext.BaseDirectory，不改变既有装载语义。
    internal PersistencePlanCompiler(string storageTypeMapPath, string platformFoundationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageTypeMapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformFoundationRoot);
        _typeMap = new JsonStorageTypeMapLoader().LoadFromFile(storageTypeMapPath);
        _platformFoundationRoot = Path.GetFullPath(platformFoundationRoot);
    }

    /// <inheritdoc/>
    public PersistencePlanCompilationResult Compile(
        CanonicalModulePlan modules,
        PersistenceProvider provider)
        => Compile(modules, provider, retainedDeployment: null);

    /// <summary>编译活动贡献并合并成功或待恢复候选的可信存储约束；实际应用进度仍由迁移执行器核对。</summary>
    public PersistencePlanCompilationResult Compile(CanonicalModulePlan modules, PersistenceProvider provider, CompiledPersistencePlan? retainedDeployment)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var diagnostics = new List<PersistencePlanDiagnostic>();
        var models = new List<ModelDefinition>();
        var streams = new List<ModuleMigrationStream>();

        foreach (var module in modules.Modules.OrderBy(static item => item.ModuleId, StringComparer.Ordinal))
        {
            CompileModule(module, provider, models, streams, diagnostics);
        }

        AddPlatformStream(modules, provider, models, streams, diagnostics);
        var activeOwners = modules.Modules.Select(module => module.ModuleId).ToHashSet(StringComparer.Ordinal);
        var retainedOwners = retainedDeployment?.Models.Select(model => model.OwnerModuleId)
            .Where(owner => !activeOwners.Contains(owner)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
        if (retainedDeployment is not null)
        {
            if (retainedDeployment.Provider != provider)
                diagnostics.Add(new("PERSISTENCE_DEPLOYMENT_PROVIDER_MISMATCH", "platform.persistence", provider.ToString(), string.Empty, "Retained deployment provider differs from the candidate."));
            foreach (var previous in retainedDeployment.Models)
            {
                if (activeOwners.Contains(previous.OwnerModuleId) && !models.Any(model => model.OwnerModuleId == previous.OwnerModuleId && model.Name == previous.Name))
                    diagnostics.Add(new("PERSISTENCE_DEPLOYED_MODEL_IDENTITY_CHANGED", previous.OwnerModuleId, previous.Name, string.Empty, "A deployed model technical identity cannot be renamed or removed while its data is retained."));
                if (models.Any(model => string.Equals(model.Name, previous.Name, StringComparison.OrdinalIgnoreCase) && model.OwnerModuleId != previous.OwnerModuleId)
                    || models.Any(model => string.Equals(model.SchemaName, previous.SchemaName, StringComparison.OrdinalIgnoreCase) && string.Equals(model.TableName, previous.TableName, StringComparison.OrdinalIgnoreCase) && model.OwnerModuleId != previous.OwnerModuleId))
                    diagnostics.Add(new("PERSISTENCE_DEPLOYED_IDENTITY_CONFLICT", previous.OwnerModuleId, previous.Name, string.Empty, "A retained model identity or physical target cannot change owner."));
            }
            models.AddRange(retainedDeployment.Models.Where(model => retainedOwners.Contains(model.OwnerModuleId, StringComparer.Ordinal)));
            var currentStreams = streams.Select(stream => stream.ModuleId).ToHashSet(StringComparer.Ordinal);
            streams.AddRange(retainedDeployment.Streams.Where(stream => !currentStreams.Contains(stream.ModuleId)));
        }
        AddGlobalConflictDiagnostics(modules, models, streams, diagnostics, retainedOwners);
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
            .OrderBy(static stream => stream.IsPlatformStream ? 0 : 1)
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
            retainedModuleIds = retainedOwners,
            models = orderedModels,
            streams = orderedStreams.Select(static stream => new
            {
                stream.ModuleId,
                provider = stream.Provider.ToString(),
                stream.BundleChecksum,
                stream.ModelDigest,
                stream.ExpectedFrontier,
                stream.IsPlatformStream,
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
                orderedStreams) { RetainedModuleIds = retainedOwners },
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

    // 按模块可信 authoring 编译独立 owner 的全局模型及迁移流。
    private void CompileModule(
        CanonicalModulePlanModule module,
        PersistenceProvider provider,
        List<ModelDefinition> models,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var contribution = module.Persistence;
        var isTrustedBuiltIn = module.Provenance.AuthenticatesBuiltIn(module);
        if (contribution is null || contribution.ModelPaths.Count == 0)
        {
            return;
        }

        if (!isTrustedBuiltIn && provider is not PersistenceProvider.SqlServer)
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
            var artifact = ResolveContainedArtifact(module, relativePath, diagnostics);
            if (artifact is null)
            {
                continue;
            }

            try
            {
                var document = _modelLoader.Load(artifact.Bytes.Span, artifact.DisplayPath);
                if (!string.Equals(document.ModuleId, module.ModuleId, StringComparison.Ordinal))
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_MODEL_OWNER_MISMATCH",
                        module.ModuleId,
                        document.Model.Name,
                        artifact.DisplayPath,
                        $"Model '{document.Model.Name}' declares owner '{document.ModuleId}' instead of '{module.ModuleId}'."));
                    continue;
                }

                if (!module.ModelKeys.Contains(document.Model.Name, StringComparer.Ordinal))
                {
                    diagnostics.Add(new PersistencePlanDiagnostic(
                        "PERSISTENCE_MODEL_KEY_UNDECLARED",
                        module.ModuleId,
                        document.Model.Name,
                        artifact.DisplayPath,
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
                    artifact.DisplayPath,
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
        if (matchingBundles.Length == 0 && isTrustedBuiltIn)
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

    private void AddPlatformStream(
        CanonicalModulePlan modules,
        PersistenceProvider provider,
        IReadOnlyList<ModelDefinition> models,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var builtInOwners = modules.Modules
            .Where(static module => module.Provenance.AuthenticatesBuiltIn(module))
            .Select(static module => module.ModuleId)
            .ToHashSet(StringComparer.Ordinal);
        var moduleOwnedMigrationStreams = streams
            .Where(static stream => !stream.IsPlatformStream)
            .Select(static stream => stream.ModuleId)
            .ToHashSet(StringComparer.Ordinal);
        var platformModels = models
            .Where(model =>
                builtInOwners.Contains(model.OwnerModuleId)
                && !moduleOwnedMigrationStreams.Contains(model.OwnerModuleId))
            .ToArray();
        if (platformModels.Length == 0)
        {
            return;
        }

        var modelDigest = PersistenceModelDigest.Compute(platformModels);
        if (provider is PersistenceProvider.SqlServer)
        {
            AddSqlServerPlatformFoundation(modelDigest, streams, diagnostics);
            return;
        }

        var (assemblyName, frontier) = provider switch
        {
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
            "platform.core:provider-baseline",
            modelDigest,
            modelDigest,
            frontier,
            IsPlatformStream: true,
            Array.Empty<CompiledMigrationStep>()));
    }

    // 从 Host 冻结输出读取 platform foundation bundle，并按共享 manifest 规则编译平台流。
    private void AddSqlServerPlatformFoundation(
        string modelDigest,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        const string moduleId = "platform.foundation";
        var assemblyPath = Path.Combine(
            _platformFoundationRoot,
            "PlatformPersistence.EfCore.SqlServerMigrations.dll");
        var manifestPath = Path.Combine(
            _platformFoundationRoot,
            "PlatformFoundation.SqlServerMigrations.json");
        try
        {
            var assemblyBytes = File.ReadAllBytes(assemblyPath);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifest = MigrationManifestIntegrity.Parse(manifestBytes);
            var manifestSteps = MigrationManifestIntegrity.Validate(
                moduleId,
                PersistenceProvider.SqlServer,
                manifest,
                assemblyBytes,
                manifestPath,
                modelDigest);
            var migrations = CompileManifestSteps(manifestSteps);
            streams.Add(new ModuleMigrationStream(
                moduleId,
                PersistenceProvider.SqlServer,
                assemblyPath,
                manifestPath,
                manifest.BundleChecksum,
                modelDigest,
                migrations[^1].MigrationId,
                IsPlatformStream: true,
                migrations)
            {
                AssemblyImage = new VerifiedArtifactImage(assemblyBytes),
                ManifestImage = new VerifiedArtifactImage(manifestBytes)
            });
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_PLATFORM_FOUNDATION_INVALID",
                moduleId,
                PersistenceProvider.SqlServer.ToString(),
                manifestPath,
                exception.Message));
        }
    }

    // 从已认证模块快照编译一个 migration bundle，失败时保持既有稳定 diagnostic。
    private void CompileBundle(
        CanonicalModulePlanModule module,
        PersistenceProvider provider,
        ModuleMigrationBundleReference bundle,
        string modelDigest,
        List<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var assemblyArtifact = ResolveContainedArtifact(module, bundle.AssemblyPath, diagnostics);
        var manifestArtifact = ResolveContainedArtifact(module, bundle.ManifestPath, diagnostics);
        if (assemblyArtifact is null || manifestArtifact is null)
        {
            return;
        }

        try
        {
            var manifest = MigrationManifestIntegrity.Parse(manifestArtifact.Bytes.Span);
            var manifestSteps = MigrationManifestIntegrity.Validate(
                module.ModuleId,
                provider,
                manifest,
                assemblyArtifact.Bytes.Span,
                manifestArtifact.DisplayPath,
                modelDigest);
            var migrations = CompileManifestSteps(manifestSteps);
            streams.Add(new ModuleMigrationStream(
                module.ModuleId,
                provider,
                assemblyArtifact.DisplayPath,
                manifestArtifact.DisplayPath,
                manifest.BundleChecksum,
                modelDigest,
                migrations[^1].MigrationId,
                IsPlatformStream: false,
                migrations)
            {
                AssemblyImage = new VerifiedArtifactImage(assemblyArtifact.Bytes.Span),
                ManifestImage = new VerifiedArtifactImage(manifestArtifact.Bytes.Span)
            });
        }
        catch (MigrationManifestValidationException exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                exception.Code,
                module.ModuleId,
                exception.CanonicalKey ?? provider.ToString(),
                manifestArtifact.DisplayPath,
                exception.Message));
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_MIGRATION_MANIFEST_INVALID",
                module.ModuleId,
                provider.ToString(),
                manifestArtifact.DisplayPath,
                exception.Message));
        }
    }

    // 把已校验且已排序的 manifest 节点投射为运行时不可变步骤。
    private static CompiledMigrationStep[] CompileManifestSteps(IReadOnlyList<MigrationManifestStep> migrations) =>
        migrations
            .Select(static migration => new CompiledMigrationStep(
                migration.Sequence,
                migration.MigrationId,
                migration.PreviousContentDigest,
                migration.ContentDigest,
                migration.FromModelDigest,
                migration.ToModelDigest,
                migration.Compatibility))
            .ToArray();

    private static ResolvedArtifact? ResolveContainedArtifact(
        CanonicalModulePlanModule module,
        string relativePath,
        List<PersistencePlanDiagnostic> diagnostics)
    {
        var trustedSource = module.Provenance.GetAuthenticatedBuiltInSource(module);
        if (trustedSource is not null)
        {
            try
            {
                var snapshot = trustedSource.GetSnapshotForConsumption(module);
                return new ResolvedArtifact(
                    Path.GetFullPath(Path.Combine(module.PackageRoot, relativePath)),
                    snapshot.ReadFile(relativePath));
            }
            catch (Exception exception)
            {
                diagnostics.Add(new PersistencePlanDiagnostic(
                    "PERSISTENCE_ARTIFACT_PATH_INVALID",
                    module.ModuleId,
                    relativePath,
                    module.Provenance.SourcePath,
                    exception.Message));
                return null;
            }
        }

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
        if (!PathContainment.Contains(root, fullPath)
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

        try
        {
            return new ResolvedArtifact(
                fullPath,
                PathContainment.ReadContainedFile(root, fullPath));
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PersistencePlanDiagnostic(
                "PERSISTENCE_ARTIFACT_PATH_INVALID",
                module.ModuleId,
                relativePath,
                fullPath,
                exception.Message));
            return null;
        }
    }

    private sealed record ResolvedArtifact(string DisplayPath, ReadOnlyMemory<byte> Bytes);

    private static string ResolveSchemaName(CanonicalModulePlanModule module)
    {
        if (module.Provenance.AuthenticatesBuiltIn(module))
        {
            return "dbo";
        }

        var normalized = NonSchemaCharacterPattern()
            .Replace(module.ModuleId.ToLowerInvariant(), "_")
            .Trim('_');
        return $"np_{normalized}";
    }

    // 保留存储引用核对 owner 和物理闭合，活动引用还必须满足能力依赖；存储不授予访问资格。
    private static void AddGlobalConflictDiagnostics(
        CanonicalModulePlan modules,
        IReadOnlyList<ModelDefinition> models,
        IReadOnlyList<ModuleMigrationStream> streams,
        List<PersistencePlanDiagnostic> diagnostics,
        IReadOnlyList<string> retainedOwners)
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

        var modelsByLogicalKey = models.ToLookup(static model => model.LogicalKey, StringComparer.Ordinal);
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

                if (foreignKey.PrincipalOwnerModuleId is { } principalOwner && principalOwner != principalModel.OwnerModuleId
                    || !retainedOwners.Contains(model.OwnerModuleId, StringComparer.Ordinal) && !ModuleDependencyPolicy.Allows(modules, model.OwnerModuleId, principalModel.OwnerModuleId))
                {
                    diagnostics.Add(new PersistencePlanDiagnostic("PERSISTENCE_FOREIGN_KEY_OWNER_INVALID",
                        model.OwnerModuleId, foreignKey.Name, string.Empty,
                        $"Foreign key '{foreignKey.Name}' references an undeclared owner or unavailable dependency '{principalModel.OwnerModuleId}'."));
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

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSchemaCharacterPattern();

}
