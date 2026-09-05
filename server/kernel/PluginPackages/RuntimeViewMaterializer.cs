using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.ModulePlanning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.Kernel.PluginPackages;

/// <summary>
/// 把一个已定位的 Model authoring 文档转换为 runtime Model 字节。
/// </summary>
/// <param name="authoringJson">候选中的原始 Model JSON。</param>
/// <param name="sourcePath">可用于诊断模块与模型位置的候选路径。</param>
/// <returns>只写入 staging 的 runtime Model JSON。</returns>
public delegate byte[] RuntimeModelMetadataProjection(ReadOnlyMemory<byte> authoringJson, string sourcePath);

/// <summary>
/// 根据当前已启用插件重新生成运行时视图。
/// 这里的职责不是把 authoring 真源原地改写，而是生成一份可整体替换、可失败回退的 runtime/public 投影视图。
/// </summary>
public class RuntimeViewMaterializer
{
    private static readonly System.Text.Json.JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly InstalledPluginStateStore _stateStore;
    private readonly FrontendRuntimeManifestBuilder _frontendManifestBuilder;
    private readonly LocalizationRuntimeManifestBuilder _localizationManifestBuilder;
    private readonly MetadataRuntimeManifestBuilder _metadataRuntimeManifestBuilder;
    private readonly RuntimeModelMetadataProjection _modelMetadataProjection;
    private readonly Action<string, string> _moveDirectory;
    private readonly TrustedBuiltInModuleSource? _builtInModuleSource;
    private readonly ModuleCapsuleCandidateReader _candidateReader = new();
    private readonly CanonicalModulePlanCompiler _compiler = new();

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore)
        : this(stateStore, new FrontendRuntimeManifestBuilder())
    {
    }

    // 便捷构造保留原始 Model 字节，建模解释由 Host 显式组合。
    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore, FrontendRuntimeManifestBuilder frontendManifestBuilder)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            Directory.Move,
            null,
            IdentityModelMetadataProjection)
    {
    }

    // 内建模块便捷构造保留原始 Model 字节，避免 Kernel 自带建模规则。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        TrustedBuiltInModuleSource builtInModuleSource)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            Directory.Move,
            builtInModuleSource,
            IdentityModelMetadataProjection)
    {
    }

    // Host 通过此组合点同时提供内建快照与真实 Model runtime projector。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        TrustedBuiltInModuleSource? builtInModuleSource,
        RuntimeModelMetadataProjection modelMetadataProjection)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            Directory.Move,
            builtInModuleSource,
            modelMetadataProjection)
    {
    }

    // 测试可替换目录切换动作，同时继续使用无语义的 Model identity projection。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        Action<string, string> moveDirectory)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            moveDirectory,
            null,
            IdentityModelMetadataProjection)
    {
    }

    // 测试可替换全部 manifest builder 与目录切换动作。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        LocalizationRuntimeManifestBuilder localizationManifestBuilder,
        MetadataRuntimeManifestBuilder metadataRuntimeManifestBuilder,
        Action<string, string> moveDirectory)
        : this(
            stateStore,
            frontendManifestBuilder,
            localizationManifestBuilder,
            metadataRuntimeManifestBuilder,
            moveDirectory,
            null,
            IdentityModelMetadataProjection)
    {
    }

    // 兼容现有内建快照组合，并把 Model 处理保持为 identity projection。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        LocalizationRuntimeManifestBuilder localizationManifestBuilder,
        MetadataRuntimeManifestBuilder metadataRuntimeManifestBuilder,
        Action<string, string> moveDirectory,
        TrustedBuiltInModuleSource? builtInModuleSource)
        : this(
            stateStore,
            frontendManifestBuilder,
            localizationManifestBuilder,
            metadataRuntimeManifestBuilder,
            moveDirectory,
            builtInModuleSource,
            IdentityModelMetadataProjection)
    {
    }

    // 最终构造统一持有 staging、快照与 Model 投影依赖。
    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        LocalizationRuntimeManifestBuilder localizationManifestBuilder,
        MetadataRuntimeManifestBuilder metadataRuntimeManifestBuilder,
        Action<string, string> moveDirectory,
        TrustedBuiltInModuleSource? builtInModuleSource,
        RuntimeModelMetadataProjection modelMetadataProjection)
    {
        _stateStore = stateStore;
        _frontendManifestBuilder = frontendManifestBuilder;
        _localizationManifestBuilder = localizationManifestBuilder;
        _metadataRuntimeManifestBuilder = metadataRuntimeManifestBuilder;
        _modelMetadataProjection = modelMetadataProjection;
        _moveDirectory = moveDirectory;
        _builtInModuleSource = builtInModuleSource;
    }

    // 未组合建模能力的低层调用保持原始字节，Kernel 不自行解释 Model 语义。
    private static byte[] IdentityModelMetadataProjection(ReadOnlyMemory<byte> authoringJson, string sourcePath)
    {
        _ = sourcePath;
        return authoringJson.ToArray();
    }

    public CanonicalModulePlan Materialize(string installRoot)
    {
        var enabledRecords = _stateStore.LoadAllEnabled();
        var installedCandidates = _candidateReader.ReadInstalled(enabledRecords);
        var candidates = installedCandidates.AsEnumerable();
        if (_builtInModuleSource is not null)
        {
            var installedModuleIds = installedCandidates
                .Select(static candidate => candidate.ModuleId)
                .ToHashSet(StringComparer.Ordinal);
            // B11 之前由 compatibility resolver 选择同 ID 的已安装版本；编译器仍负责拒绝大小写折叠碰撞。
            candidates = candidates.Concat(
                _candidateReader.ReadTrustedBuiltIn(_builtInModuleSource)
                    .Where(candidate => !installedModuleIds.Contains(candidate.ModuleId)));
        }

        var compilation = _compiler.Compile("active-module-runtime", candidates);
        if (!compilation.IsSuccess || compilation.Plan is null)
        {
            var details = string.Join(
                Environment.NewLine,
                compilation.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}"));
            throw new InvalidOperationException($"Canonical Module Plan compilation failed.{Environment.NewLine}{details}");
        }

        var plan = compilation.Plan;
        using var stage = Stage(installRoot, plan);
        using var commit = stage.BeginCommit();
        commit.Complete();
        return plan;
    }

    /// <summary>
    /// 只读验证候选中所有将按普通 Model metadata 发布的文档。
    /// </summary>
    /// <param name="plan">已冻结且尚未产生启动副作用的候选计划。</param>
    internal void ValidateModelMetadata(CanonicalModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var module in plan.Modules)
        {
            foreach (var document in EnumerateCandidateModelMetadata(module))
            {
                _modelMetadataProjection(document.Bytes, document.SourcePath);
            }
        }
    }

    /// <summary>
    /// 从已编译计划构建尚不可见的 runtime/public staging；不会读取或修改启用状态。
    /// </summary>
    public RuntimeViewProjectionStage Stage(string installRoot, CanonicalModulePlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(plan);

        var resolvedInstallRoot = Path.GetFullPath(installRoot);
        // 所有输出先进入 staging；只有全部成功后才会替换正式 runtime/public，
        // 避免中途失败时留下半更新的运行时视图。
        var stagingRoot = Path.Combine(resolvedInstallRoot, $".materialize-staging-{Guid.NewGuid():N}");
        var stagedRuntimeRoot = Path.Combine(stagingRoot, "runtime");
        var stagedBackendViewRoot = Path.Combine(stagedRuntimeRoot, "backend-load-view");
        var stagedPublicRoot = Path.Combine(stagingRoot, "public");

        try
        {
            Directory.CreateDirectory(stagedBackendViewRoot);
            Directory.CreateDirectory(stagedPublicRoot);

            foreach (var module in plan.Modules)
            {
                VerifiedModuleArtifactSnapshot? builtInSnapshot = null;
                if (module.Provenance.AuthenticatesBuiltIn(module))
                {
                    if (_builtInModuleSource is null)
                    {
                        throw new InvalidOperationException(
                            $"Authenticated built-in module '{module.ModuleId}' requires its Host trust source.");
                    }

                    // capability 只签发一次不可变工件快照；投影阶段不得重新枚举 authoring 路径。
                    builtInSnapshot = _builtInModuleSource.GetSnapshotForConsumption(module);
                }

                MaterializeBackendView(stagedBackendViewRoot, module, builtInSnapshot);
                StageFrontendAssets(stagedPublicRoot, module, builtInSnapshot);
                StageModuleMetadata(stagedRuntimeRoot, module, builtInSnapshot);
                StagePlatformResources(stagedRuntimeRoot, module, builtInSnapshot);
            }

            var frontendManifestPath = Path.Combine(stagedRuntimeRoot, "frontend-plugin-manifest.json");
            File.WriteAllText(frontendManifestPath, _frontendManifestBuilder.Build(plan));

            var enabledModuleOrder = plan.Modules
                .Select(static module => module.ModuleId)
                .ToArray();
            var frontendModuleIds = plan.Modules
                .Where(static module => !string.IsNullOrWhiteSpace(module.FrontendEntry))
                .Select(static module => module.ModuleId)
                .ToArray();
            var localizationManifestPath = Path.Combine(stagedRuntimeRoot, "localization-manifest.json");
            File.WriteAllText(
                localizationManifestPath,
                BindProjection(_localizationManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder), plan));

            var metadataRuntimeManifestPath = Path.Combine(stagedRuntimeRoot, "metadata-runtime-manifest.json");
            File.WriteAllText(
                metadataRuntimeManifestPath,
                BindProjection(
                    _metadataRuntimeManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder, frontendModuleIds),
                    plan));

            File.WriteAllBytes(Path.Combine(stagedRuntimeRoot, "canonical-module-plan.json"), plan.CanonicalBytes);
            File.WriteAllText(
                Path.Combine(stagedRuntimeRoot, "active-module-runtime.json"),
                new ActiveModuleRuntimeCatalogBuilder(_frontendManifestBuilder.PublicBaseUrl).Build(plan));

            return new RuntimeViewProjectionStage(
                this,
                resolvedInstallRoot,
                stagingRoot,
                stagedRuntimeRoot,
                stagedPublicRoot,
                plan);
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            throw;
        }
    }

    private void MaterializeBackendView(
        string backendViewRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot? builtInSnapshot)
    {
        if (string.IsNullOrWhiteSpace(module.BackendEntry))
        {
            return;
        }

        if (module.Provenance.AuthenticatesBuiltIn(module))
        {
            MaterializeBuiltInBackendView(backendViewRoot, module, builtInSnapshot!);
            return;
        }

        var backendRoot = Path.Combine(module.PackageRoot, "backend");
        var backendManifestPath = Path.Combine(backendRoot, "plugin.backend.json");
        var backendManifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(backendManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Backend manifest '{backendManifestPath}' deserialized to null.");

        var targetDirectory = Path.Combine(backendViewRoot, module.ModuleId);
        Directory.CreateDirectory(targetDirectory);

        // backend-load-view 是运行时消费的统一后端视图。
        // 这里保留包内结构，同时把后端 manifest 规范化为 plugin.json，避免运行时再关心包内命名差异。
        CopyDirectory(backendRoot, targetDirectory);
        File.Copy(backendManifestPath, Path.Combine(targetDirectory, "plugin.json"), overwrite: true);

        if (!string.Equals(backendManifest.EntryAssembly, Path.GetFileName(backendManifest.EntryAssembly), StringComparison.Ordinal))
        {
            var resolvedEntryAssemblyPath = ResolveContainedPath(backendRoot, backendManifest.EntryAssembly, "EntryAssembly");
            File.Copy(resolvedEntryAssemblyPath, Path.Combine(targetDirectory, backendManifest.EntryAssembly), overwrite: true);
        }
    }

    private void MaterializeBuiltInBackendView(
        string backendViewRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot snapshot)
    {
        if (_builtInModuleSource is null)
        {
            throw new InvalidOperationException(
                $"Authenticated built-in module '{module.ModuleId}' requires its Host trust source.");
        }

        var targetDirectory = Path.Combine(backendViewRoot, module.ModuleId);
        Directory.CreateDirectory(targetDirectory);
        var targetEntryName = Path.GetFileName(module.BackendEntry!);
        File.WriteAllBytes(
            Path.Combine(targetDirectory, targetEntryName),
            snapshot.ReadDistributionFile(module.BackendEntry!));

        var manifest = JsonNode.Parse(snapshot.ReadText("plugin.json"))?.AsObject()
            ?? throw new JsonException($"Built-in module manifest for '{module.ModuleId}' deserialized to null.");
        manifest["entryAssembly"] = targetEntryName;
        File.WriteAllText(
            Path.Combine(targetDirectory, "plugin.json"),
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void StageFrontendAssets(
        string publicRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            StageBuiltInFrontendAssets(publicRoot, module, snapshot);
            return;
        }

        var frontendRoot = Path.GetFullPath(Path.Combine(module.PackageRoot, "frontend"));
        var frontendManifestPath = Path.Combine(frontendRoot, "plugin.frontend.json");
        if (!File.Exists(frontendManifestPath))
        {
            return;
        }
        var frontendManifest = System.Text.Json.JsonSerializer.Deserialize<FrontendPackageManifest>(File.ReadAllText(frontendManifestPath), ManifestJsonOptions)
            ?? throw new System.Text.Json.JsonException($"Frontend manifest '{frontendManifestPath}' deserialized to null.");

        var assetRootPath = ResolveContainedDirectoryPath(frontendRoot, frontendManifest.AssetRoot, "AssetRoot");
        var entryManifestPath = ResolveContainedFilePath(frontendRoot, frontendManifest.EntryManifest, "EntryManifest");
        var entryManifest = System.Text.Json.JsonSerializer.Deserialize<FrontendEntryManifest>(File.ReadAllText(entryManifestPath), ManifestJsonOptions)
            ?? throw new System.Text.Json.JsonException($"Frontend runtime manifest '{entryManifestPath}' deserialized to null.");
        // 这里显式验证 entry 文件存在，但不直接复制 entry 本身；
        // public 目录交付的是资产根和入口清单，真正入口仍由运行时 manifest 决定。
        ResolveContainedFilePath(frontendRoot, entryManifest.Entry, "Entry");

        var targetRoot = Path.Combine(publicRoot, module.ModuleId, module.Version);
        var assetRootName = Path.GetFileName(assetRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        CopyDirectory(assetRootPath, Path.Combine(targetRoot, assetRootName));
        File.Copy(
            entryManifestPath,
            Path.Combine(targetRoot, Path.GetFileName(entryManifestPath)),
            overwrite: true);
    }

    private static void StageBuiltInFrontendAssets(
        string publicRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot snapshot)
    {
        const string frontendManifestPath = "frontend/plugin.frontend.json";
        if (!snapshot.ContainsFile(frontendManifestPath))
        {
            return;
        }

        var frontendManifest = JsonSerializer.Deserialize<FrontendPackageManifest>(
            snapshot.ReadFile(frontendManifestPath),
            ManifestJsonOptions)
            ?? throw new JsonException($"Frontend manifest for '{module.ModuleId}' deserialized to null.");
        var assetRootPath = ResolveSnapshotPath("frontend", frontendManifest.AssetRoot, "AssetRoot");
        var entryManifestPath = ResolveSnapshotPath("frontend", frontendManifest.EntryManifest, "EntryManifest");
        var entryManifest = JsonSerializer.Deserialize<FrontendEntryManifest>(
            snapshot.ReadFile(entryManifestPath),
            ManifestJsonOptions)
            ?? throw new JsonException($"Frontend runtime manifest for '{module.ModuleId}' deserialized to null.");
        var entryPath = ResolveSnapshotPath("frontend", entryManifest.Entry, "Entry");
        if (!snapshot.ContainsFile(entryPath))
        {
            throw new FileNotFoundException("Frontend manifest Entry file not found.", entryPath);
        }

        var targetRoot = Path.Combine(publicRoot, module.ModuleId, module.Version);
        var assetRootName = assetRootPath.Split('/').Last();
        WriteSnapshotDirectory(snapshot, assetRootPath, Path.Combine(targetRoot, assetRootName));
        File.WriteAllBytes(
            Path.Combine(targetRoot, Path.GetFileName(entryManifestPath)),
            snapshot.ReadFile(entryManifestPath));
    }

    // 先复制候选 metadata，再只在不可见 staging 中规范普通 Model JSON。
    private void StageModuleMetadata(
        string runtimeRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot? snapshot)
    {
        if (string.Equals(module.FrontendBindingMode, "standard-data", StringComparison.Ordinal))
        {
            StageStandardDataModule(runtimeRoot, module, snapshot);
            return;
        }

        var packageMetadataRoot = module.Provenance.AuthenticatesBuiltIn(module)
            ? Path.Combine(module.PackageRoot, "metadata")
            : Path.Combine(module.PackageRoot, "modules", module.ModuleId, "metadata");
        if (snapshot is null && !Directory.Exists(packageMetadataRoot))
        {
            return;
        }

        var targetMetadataRoot = Path.Combine(runtimeRoot, "modules", module.ModuleId, "metadata");
        if (snapshot is not null)
        {
            WriteSnapshotDirectory(snapshot, "metadata", targetMetadataRoot);
        }
        else
        {
            CopyDirectory(packageMetadataRoot, targetMetadataRoot);
        }

        ProjectStagedModelMetadata(targetMetadataRoot);
    }

    // staging 与 Prepare 使用同一个 Model projector；这里复验可检测 installed 工件在两阶段之间的漂移。
    private void ProjectStagedModelMetadata(string targetMetadataRoot)
    {
        var modelsRoot = Path.Combine(targetMetadataRoot, "models");
        if (!Directory.Exists(modelsRoot))
        {
            return;
        }

        foreach (var path in Directory
                     .EnumerateFiles(modelsRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var authoringBytes = File.ReadAllBytes(path);
            var runtimeBytes = _modelMetadataProjection(authoringBytes, path);
            if (!authoringBytes.AsSpan().SequenceEqual(runtimeBytes))
            {
                File.WriteAllBytes(path, runtimeBytes);
            }
        }
    }

    // 普通 Model 的候选集合与 runtime manifest 的 metadata/models 目录边界保持一致。
    private IEnumerable<(ReadOnlyMemory<byte> Bytes, string SourcePath)> EnumerateCandidateModelMetadata(
        CanonicalModulePlanModule module)
    {
        if (string.Equals(module.FrontendBindingMode, "standard-data", StringComparison.Ordinal))
        {
            yield break;
        }

        if (module.Provenance.AuthenticatesBuiltIn(module))
        {
            if (_builtInModuleSource is null)
            {
                throw new InvalidOperationException(
                    $"Authenticated built-in module '{module.ModuleId}' requires its Host trust source.");
            }

            var snapshot = _builtInModuleSource.GetSnapshotForConsumption(module);
            foreach (var relativePath in snapshot
                         .EnumerateFiles("metadata/models")
                         .Where(static path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                yield return (
                    snapshot.ReadFile(relativePath),
                    Path.Combine(snapshot.ModuleRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            yield break;
        }

        var metadataRoot = Path.Combine(module.PackageRoot, "modules", module.ModuleId, "metadata");
        var modelsRoot = Path.Combine(metadataRoot, "models");
        if (!Directory.Exists(modelsRoot))
        {
            yield break;
        }

        foreach (var path in Directory
                     .EnumerateFiles(modelsRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return (File.ReadAllBytes(path), path);
        }
    }

    // standard-data 投影只复制包内 authoring 契约，并为共享列表宿主生成受控 metadata。
    private void StageStandardDataModule(
        string runtimeRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot? snapshot)
    {
        var moduleRuntimeRoot = Path.Combine(runtimeRoot, "modules", module.ModuleId);
        var targetMetadataRoot = Path.Combine(moduleRuntimeRoot, "metadata");
        if (snapshot is not null)
        {
            WriteSnapshotDirectory(snapshot, "metadata", targetMetadataRoot);
        }
        else
        {
            var packageMetadataRoot = Path.Combine(module.PackageRoot, "metadata");
            if (Directory.Exists(packageMetadataRoot))
            {
                CopyDirectory(packageMetadataRoot, targetMetadataRoot);
            }
        }

        var manifest = snapshot is null
            ? ReadPackageObject(module.PackageRoot, "plugin.json")
            : JsonNode.Parse(snapshot.ReadFile("plugin.json"))?.AsObject()
                ?? throw new JsonException($"Standard data package '{module.ModuleId}' deserialized to null.");
        var artifacts = manifest["artifacts"]?.AsObject()
            ?? throw new JsonException($"Standard data package '{module.ModuleId}' is missing artifacts.");
        var blueprintRelativePath = RequiredString(artifacts, "blueprint", module.ModuleId);
        var projectionRelativePath = RequiredString(artifacts, "standardProjection", module.ModuleId);
        var blueprintBytes = snapshot?.ReadFile(ResolveSnapshotPackagePath(blueprintRelativePath, "Blueprint"));
        var projectionBytes = snapshot?.ReadFile(ResolveSnapshotPackagePath(projectionRelativePath, "StandardProjection"));
        var blueprintPath = snapshot is null
            ? ResolveContainedFilePath(module.PackageRoot, blueprintRelativePath, "Blueprint")
            : blueprintRelativePath;
        var projectionPath = snapshot is null
            ? ResolveContainedFilePath(module.PackageRoot, projectionRelativePath, "StandardProjection")
            : projectionRelativePath;
        var targetBlueprintPath = Path.Combine(moduleRuntimeRoot, "authoring", "newp.plugin.json");
        var targetProjectionPath = Path.Combine(moduleRuntimeRoot, "projections", "standard-data.json");

        if (snapshot is null)
        {
            CopyContract(blueprintPath, targetBlueprintPath);
            CopyContract(projectionPath, targetProjectionPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetBlueprintPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetProjectionPath)!);
            File.WriteAllBytes(targetBlueprintPath, blueprintBytes!);
            File.WriteAllBytes(targetProjectionPath, projectionBytes!);
        }

        var blueprint = JsonNode.Parse(blueprintBytes ?? File.ReadAllBytes(blueprintPath))?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' deserialized to null.");
        var projection = JsonNode.Parse(projectionBytes ?? File.ReadAllBytes(projectionPath))?.AsObject()
            ?? throw new JsonException($"Standard data projection for '{module.ModuleId}' deserialized to null.");
        WriteStandardDataMetadata(targetMetadataRoot, module, blueprint, projection);
    }

    // 生成列表与导航投影；详情展示结构始终由包内显式 Detail View 拥有。
    private void WriteStandardDataMetadata(
        string metadataRoot,
        CanonicalModulePlanModule module,
        JsonObject blueprint,
        JsonObject projection)
    {
        var standardData = blueprint["standardData"]?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' is missing standardData.");
        var resources = standardData["resources"]?.AsArray()
            ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' is missing resources.");
        var projectedResources = projection["resources"]?.AsArray()
            ?? throw new JsonException($"Standard data projection for '{module.ModuleId}' is missing resources.");
        var navigation = blueprint["navigation"]?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' is missing navigation.");
        var moduleSlug = RequiredString(projection, "moduleSlug", module.ModuleId);
        var navigationItems = new List<object>
        {
            new
            {
                key = moduleSlug,
                titleKey = RequiredString(navigation, "group", module.ModuleId),
                icon = "database",
                order = navigation["order"]?.GetValue<int>() ?? 0
            }
        };

        foreach (var resourceNode in resources)
        {
            var resource = resourceNode?.AsObject()
                ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' contains an invalid resource.");
            var resourceKey = RequiredString(resource, "key", module.ModuleId);
            var resourceSlug = ToRouteToken(resourceKey);
            var resourceProjection = projectedResources
                .Select(static item => item?.AsObject())
                .SingleOrDefault(item => item is not null
                    && string.Equals(item["resourceKey"]?.GetValue<string>(), resourceKey, StringComparison.Ordinal))
                ?? throw new JsonException($"Standard data resource '{module.ModuleId}.{resourceKey}' has no projection.");
            var pageRoute = RequiredString(resourceProjection, "pageRoute", module.ModuleId);
            var declaredDetailViewKey = OptionalString(resource, "detailViewKey", module.ModuleId);
            var projectedDetailViewKey = OptionalString(resourceProjection, "detailViewKey", module.ModuleId);
            if (!string.Equals(declaredDetailViewKey, projectedDetailViewKey, StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"Standard data resource '{module.ModuleId}.{resourceKey}' has inconsistent detailViewKey projection.");
            }

            if (declaredDetailViewKey is not null)
            {
                ValidateDeclaredDetailView(metadataRoot, module.ModuleId, resourceKey, declaredDetailViewKey);
            }

            var permissionCodes = resourceProjection["permissionCodes"]?.AsArray()
                .Select(static item => item?.GetValue<string>() ?? string.Empty)
                .Where(static code => code.Length > 0)
                .ToArray() ?? [];
            var viewPermission = permissionCodes.Single(code => code.EndsWith(".View", StringComparison.Ordinal));
            var createPermission = permissionCodes.Single(code => code.EndsWith(".Create", StringComparison.Ordinal));
            var updatePermission = permissionCodes.Single(code => code.EndsWith(".Update", StringComparison.Ordinal));
            var listViewKey = $"{moduleSlug}.{resourceSlug}.list";
            var fields = resource["fields"]?.AsArray()
                ?? throw new JsonException($"Standard data resource '{module.ModuleId}.{resourceKey}' is missing fields.");
            var hasEnabledField = fields
                .Select(static fieldNode => fieldNode?.AsObject())
                .Any(static field => string.Equals(
                    field?["name"]?.GetValue<string>(),
                    "IsEnabled",
                    StringComparison.Ordinal));

            navigationItems.Add(new
            {
                key = $"{moduleSlug}.{resourceSlug}",
                titleKey = RequiredString(resource, "displayName", module.ModuleId),
                icon = "table",
                parentKey = moduleSlug,
                path = pageRoute,
                order = navigation["order"]?.GetValue<int>() ?? 0,
                entryType = "standard-list",
                targetKey = listViewKey,
                permissions = new[] { viewPermission }
            });

            var columns = fields.Select(fieldNode =>
            {
                var field = fieldNode?.AsObject()
                    ?? throw new JsonException($"Standard data resource '{module.ModuleId}.{resourceKey}' contains an invalid field.");
                var fieldName = RequiredString(field, "name", module.ModuleId);
                return new
                {
                    key = ToRouteToken(fieldName),
                    labelKey = fieldName,
                    source = "model",
                    field = fieldName,
                    sortable = true
                };
            }).ToArray();
            var modelFields = fields.Select(fieldNode =>
            {
                var field = fieldNode?.AsObject()
                    ?? throw new JsonException($"Standard data resource '{module.ModuleId}.{resourceKey}' contains an invalid field.");
                var fieldName = RequiredString(field, "name", module.ModuleId);
                var fieldType = RequiredString(field, "type", module.ModuleId);
                var required = field["required"]?.GetValue<bool>()
                    ?? throw new JsonException(
                        $"Standard data field '{module.ModuleId}.{resourceKey}.{fieldName}' is missing required semantics.");
                return new
                {
                    name = fieldName,
                    storage = new
                    {
                        type = MapStandardDataStorageType(fieldType),
                        nullable = !required
                    },
                    uiType = MapStandardDataUiType(fieldType),
                    form = new
                    {
                        create = new { mode = "editable", required },
                        edit = new { mode = "editable", required }
                    }
                };
            }).ToArray();
            var rowActions = new List<object>();
            if (declaredDetailViewKey is not null)
            {
                rowActions.Add(new { key = "view", labelKey = "Action_View", standardAction = "view", variant = (string?)null, permissions = Array.Empty<string>() });
            }

            rowActions.Add(new { key = "edit", labelKey = "Action_Edit", standardAction = "edit", variant = (string?)null, permissions = new[] { updatePermission } });
            if (hasEnabledField)
            {
                // standard-data.v1 没有删除策略声明；这里只投影描述符已支持的停启能力，
                // 避免页面暴露一个必然被 DeletePolicy.None 拒绝的删除动作。
                rowActions.Add(new
                {
                    key = "disable",
                    labelKey = "Action_Disable",
                    standardAction = "disable",
                    variant = "danger",
                    permissions = new[] { updatePermission },
                    visibleWhen = new { field = "IsEnabled", @operator = "equals", value = true }
                });
                rowActions.Add(new
                {
                    key = "enable",
                    labelKey = "Action_Enable",
                    standardAction = "enable",
                    variant = (string?)null,
                    permissions = new[] { updatePermission },
                    visibleWhen = new { field = "IsEnabled", @operator = "equals", value = false }
                });
            }

            var listView = new JsonObject
            {
                ["key"] = listViewKey,
                ["dicKey"] = $"{resourceKey}ListView",
                ["titleKey"] = RequiredString(resource, "displayName", module.ModuleId),
                ["mode"] = "list",
                ["model"] = resourceKey,
                ["editorMode"] = "drawer",
                ["columns"] = JsonSerializer.SerializeToNode(columns),
                ["pageActions"] = JsonSerializer.SerializeToNode(new object[]
                {
                    new { key = "create", labelKey = "Action_Create", standardAction = "create", variant = "primary", permissions = new[] { createPermission } },
                    new { key = "refresh", labelKey = "Action_Refresh", standardAction = "refresh", variant = (string?)null, permissions = Array.Empty<string>() }
                }),
                ["rowActions"] = JsonSerializer.SerializeToNode(rowActions)
            };
            if (declaredDetailViewKey is not null)
            {
                listView["detailViewKey"] = declaredDetailViewKey;
            }

            WriteJson(
                Path.Combine(metadataRoot, "views", $"{resourceKey}List.view.json"),
                new JsonObject
                {
                    ["schemaVersion"] = "1.0",
                    ["type"] = "view",
                    ["view"] = listView
                });
            var modelPath = Path.Combine(metadataRoot, "models", $"{resourceKey}.model.json");
            var modelDocument = new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["type"] = "model",
                [resourceKey] = JsonSerializer.SerializeToNode(new
                {
                    kind = "Entity",
                    sections = new[]
                    {
                        new
                        {
                            key = "basic",
                            titleKey = "Section_Basic",
                            fields = modelFields.Select(static field => field.name).ToArray()
                        }
                    },
                    fields = modelFields
                })
            };
            WriteProjectedModelJson(modelPath, modelDocument);
        }

        WriteJson(
            Path.Combine(metadataRoot, "navigation", "standard-data.nav.json"),
            new
            {
                schemaVersion = "1.0",
                type = "nav",
                navigation = new { items = navigationItems }
            });
    }

    // standard-data 的生成模型先在内存中走与普通 Model 相同的投影，再以单个文件写入未发布 staging。
    private void WriteProjectedModelJson(string path, JsonObject modelDocument)
    {
        var generatedBytes = Encoding.UTF8.GetBytes(
            modelDocument.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        var runtimeBytes = _modelMetadataProjection(generatedBytes, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, runtimeBytes);
    }

    // Blueprint 的公开类型映射到现有 Model storage 词汇，不引入新的 profile 字段或运行时 fallback。
    private static string MapStandardDataStorageType(string fieldType) => fieldType switch
    {
        "string" => "string",
        "date" => "date",
        "dateTime" => "datetime",
        "boolean" => "bool",
        "integer" => "int",
        "decimal" => "decimal",
        _ => throw new JsonException($"Standard data field type '{fieldType}' is unsupported.")
    };

    // Shell 表单种类与既有 standard-data 前端投影保持一致，create/edit 都消费同一 Required 事实。
    private static string MapStandardDataUiType(string fieldType) => fieldType switch
    {
        "boolean" => "switch",
        "integer" or "decimal" => "number",
        "date" or "dateTime" => "date",
        "string" => "text",
        _ => throw new JsonException($"Standard data field type '{fieldType}' is unsupported.")
    };

    // 显式引用必须在包内唯一解析到同资源的 Detail View，避免激活后再回退或局部失败。
    private static void ValidateDeclaredDetailView(
        string metadataRoot,
        string moduleId,
        string resourceKey,
        string detailViewKey)
    {
        var viewsRoot = Path.Combine(metadataRoot, "views");
        var matches = Directory.Exists(viewsRoot)
            ? Directory.EnumerateFiles(viewsRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(path => JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                    ?? throw new JsonException($"Standard data View '{path}' deserialized to null."))
                .Select(document => document["view"]?.AsObject())
                .Where(static view => view is not null)
                .Where(view => string.Equals(
                    OptionalString(view!, "key", moduleId),
                    detailViewKey,
                    StringComparison.Ordinal))
                .Cast<JsonObject>()
                .ToArray()
            : [];
        if (matches.Length != 1)
        {
            throw new JsonException(
                $"Standard data resource '{moduleId}.{resourceKey}' detailViewKey '{detailViewKey}' must resolve to exactly one package View.");
        }

        var detailView = matches[0];
        if (!string.Equals(OptionalString(detailView, "mode", moduleId), "detail", StringComparison.Ordinal)
            || !string.Equals(OptionalString(detailView, "model", moduleId), resourceKey, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Standard data resource '{moduleId}.{resourceKey}' detailViewKey '{detailViewKey}' references the wrong View mode or model.");
        }
    }

    private static JsonObject ReadPackageObject(string packageRoot, string relativePath)
    {
        var path = ResolveContainedFilePath(packageRoot, relativePath, "PackageManifest");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new JsonException($"Package file '{relativePath}' deserialized to null.");
    }

    // 读取必需非空字符串，保持包错误在 staging 内原子失败。
    private static string RequiredString(JsonObject value, string propertyName, string moduleId) =>
        value[propertyName]?.GetValue<string>() is { Length: > 0 } result
            ? result
            : throw new JsonException($"Standard data package '{moduleId}' is missing {propertyName}.");

    // 读取可选非空字符串；显式 null、空值或错误类型均视为损坏的包契约。
    private static string? OptionalString(JsonObject value, string propertyName, string moduleId)
    {
        if (!value.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var result)
            && !string.IsNullOrWhiteSpace(result))
        {
            return result;
        }

        throw new JsonException($"Standard data package '{moduleId}' has an invalid {propertyName}.");
    }

    private static string ToRouteToken(string value) =>
        string.Concat(value.Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"-{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static void CopyContract(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void StagePlatformResources(
        string runtimeRoot,
        CanonicalModulePlanModule module,
        VerifiedModuleArtifactSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            var snapshotTargetResourcesRoot = Path.Combine(runtimeRoot, "resources");
            WriteSnapshotDirectoryRejectingConflicts(
                snapshot,
                "resources",
                snapshotTargetResourcesRoot,
                module.ModuleId);
            return;
        }

        var packageResourcesRoot = Path.Combine(module.PackageRoot, "resources");
        if (!Directory.Exists(packageResourcesRoot))
        {
            return;
        }

        // 平台公共资源按最终生效视图汇总到同一个 resources 目录；
        // 冲突时直接失败，而不是依赖复制顺序做隐式覆盖。
        var targetResourcesRoot = Path.Combine(runtimeRoot, "resources");
        CopyDirectoryRejectingConflicts(packageResourcesRoot, targetResourcesRoot, module.ModuleId);
    }

    private static string ResolveSnapshotPath(string root, string? relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        var combined = $"{root.TrimEnd('/', '\\')}/{relativePath.Replace('\\', '/')}";
        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count <= 1)
                {
                    throw new JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var resolved = string.Join('/', segments);
        if (!resolved.StartsWith(root.TrimEnd('/', '\\') + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        return resolved;
    }

    private static string ResolveSnapshotPackagePath(string relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new JsonException($"Standard data {propertyName} resolves to an unsafe path.");
        }

        var segments = new List<string>();
        foreach (var segment in relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new JsonException($"Standard data {propertyName} resolves to an unsafe path.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static void WriteSnapshotDirectory(
        VerifiedModuleArtifactSnapshot snapshot,
        string sourceRoot,
        string destinationRoot)
    {
        foreach (var sourcePath in snapshot.EnumerateFiles(sourceRoot))
        {
            var relativePath = sourcePath[(sourceRoot.TrimEnd('/', '\\').Length + 1)..];
            var destinationPath = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, snapshot.ReadFile(sourcePath));
        }
    }

    private static void WriteSnapshotDirectoryRejectingConflicts(
        VerifiedModuleArtifactSnapshot snapshot,
        string sourceRoot,
        string destinationRoot,
        string moduleId)
    {
        foreach (var sourcePath in snapshot.EnumerateFiles(sourceRoot))
        {
            var relativePath = sourcePath[(sourceRoot.TrimEnd('/', '\\').Length + 1)..];
            var destinationPath = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bytes = snapshot.ReadFile(sourcePath);
            if (File.Exists(destinationPath))
            {
                if (File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(bytes))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Platform resource conflict detected for '{relativePath}' while materializing module '{moduleId}'.");
            }

            File.WriteAllBytes(destinationPath, bytes);
        }
    }

    private static string ResolveContainedPath(string rootPath, string? relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new System.Text.Json.JsonException($"Frontend manifest {propertyName} must not be empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new System.Text.Json.JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        // manifest 中的相对路径必须被约束在插件包目录内，防止通过 ../ 或绝对路径越界读取宿主文件。
        if (!PathContainment.Contains(fullRootPath, fullCandidatePath))
        {
            throw new System.Text.Json.JsonException($"Frontend manifest {propertyName} resolves to an unsafe path.");
        }

        return fullCandidatePath;
    }

    private static string ResolveContainedFilePath(string rootPath, string? relativePath, string propertyName)
    {
        var candidatePath = ResolveContainedPath(rootPath, relativePath, propertyName);
        if (!File.Exists(candidatePath))
        {
            throw new FileNotFoundException($"Frontend manifest {propertyName} file not found.", relativePath);
        }

        return candidatePath;
    }

    private static string ResolveContainedDirectoryPath(string rootPath, string? relativePath, string propertyName)
    {
        var candidatePath = ResolveContainedPath(rootPath, relativePath, propertyName);
        if (!Directory.Exists(candidatePath))
        {
            throw new DirectoryNotFoundException($"Frontend manifest {propertyName} directory not found: {relativePath}");
        }

        return candidatePath;
    }

    private static string BindProjection(string projectionJson, CanonicalModulePlan plan)
    {
        var projection = JsonNode.Parse(projectionJson)?.AsObject()
            ?? throw new JsonException("Runtime compatibility projection deserialized to null.");
        projection.Insert(0, "projectionHash", plan.PlanHash);
        projection.Insert(0, "snapshotId", plan.SnapshotId);
        return projection.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    internal RuntimeViewProjectionCommit BeginCommit(RuntimeViewProjectionStage stage)
    {
        var runtimeRoot = Path.Combine(stage.InstallRoot, "runtime");
        var publicRoot = Path.Combine(stage.InstallRoot, "public");
        var commitWorkspaceRoot = Path.Combine(
            stage.InstallRoot,
            $".materialize-commit-{Guid.NewGuid():N}");
        var runtimeBackupRoot = Path.Combine(commitWorkspaceRoot, "runtime-backup");
        var publicBackupRoot = Path.Combine(commitWorkspaceRoot, "public-backup");
        var runtimeExists = Directory.Exists(runtimeRoot);
        var publicExists = Directory.Exists(publicRoot);
        var runtimeCommitted = false;
        var publicCommitted = false;

        Directory.CreateDirectory(commitWorkspaceRoot);

        try
        {
            // 先把当前有效视图移动到备份目录，再提交 staging 结果。
            // 一旦后续 move 失败，catch 会按目录级别恢复，保证 runtime/public 仍指向上一版完整视图。
            if (runtimeExists)
            {
                _moveDirectory(runtimeRoot, runtimeBackupRoot);
            }

            if (publicExists)
            {
                _moveDirectory(publicRoot, publicBackupRoot);
            }

            _moveDirectory(stage.StagedRuntimeRoot, runtimeRoot);
            runtimeCommitted = true;

            _moveDirectory(stage.StagedPublicRoot, publicRoot);
            publicCommitted = true;

            return new RuntimeViewProjectionCommit(
                runtimeRoot,
                publicRoot,
                runtimeBackupRoot,
                publicBackupRoot,
                commitWorkspaceRoot,
                runtimeExists,
                publicExists,
                runtimeCommitted,
                publicCommitted);
        }
        catch
        {
            RestoreProjection(runtimeRoot, runtimeBackupRoot, runtimeExists, runtimeCommitted);
            RestoreProjection(publicRoot, publicBackupRoot, publicExists, publicCommitted);
            TryDeleteDirectory(commitWorkspaceRoot);
            throw;
        }
    }

    internal static void RestoreProjection(string destinationPath, string backupPath, bool destinationPreviouslyExisted, bool destinationWasReplaced)
    {
        if (destinationWasReplaced && Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        // 只有原目录本来存在时才回滚备份，避免把不存在的目标“恢复”成新目录。
        if (destinationPreviouslyExisted && Directory.Exists(backupPath))
        {
            Directory.Move(backupPath, destinationPath);
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void CopyDirectoryRejectingConflicts(string sourceDirectory, string destinationDirectory, string moduleId)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (File.Exists(destinationPath))
            {
                if (FilesAreIdentical(file, destinationPath))
                {
                    continue;
                }

                // 资源冲突必须显式暴露给内核处理，不能靠最后复制者获胜。
                throw new InvalidOperationException(
                    $"Platform resource conflict detected for '{relativePath}' while materializing module '{moduleId}'.");
            }

            File.Copy(file, destinationPath, overwrite: false);
        }
    }

    private static bool FilesAreIdentical(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(leftPath);
        using var rightStream = File.OpenRead(rightPath);

        while (true)
        {
            var leftByte = leftStream.ReadByte();
            var rightByte = rightStream.ReadByte();
            if (leftByte != rightByte)
            {
                return false;
            }

            if (leftByte == -1)
            {
                return true;
            }
        }
    }

    private sealed class FrontendPackageManifest
    {
        public string EntryManifest { get; set; } = string.Empty;
        public string AssetRoot { get; set; } = string.Empty;
    }

    private sealed class FrontendEntryManifest
    {
        public string Entry { get; set; } = string.Empty;
    }
}

/// <summary>
/// 尚未发布的 runtime/public 投影。Dispose 只清理本次 staging。
/// </summary>
public sealed class RuntimeViewProjectionStage : IDisposable
{
    private readonly RuntimeViewMaterializer _owner;
    private bool _commitStarted;
    private bool _disposed;

    internal RuntimeViewProjectionStage(
        RuntimeViewMaterializer owner,
        string installRoot,
        string stagingRoot,
        string stagedRuntimeRoot,
        string stagedPublicRoot,
        CanonicalModulePlan plan)
    {
        _owner = owner;
        InstallRoot = installRoot;
        StagingRoot = stagingRoot;
        StagedRuntimeRoot = stagedRuntimeRoot;
        StagedPublicRoot = stagedPublicRoot;
        Plan = plan;
    }

    internal string InstallRoot { get; }

    internal string StagingRoot { get; }

    internal string StagedRuntimeRoot { get; }

    internal string StagedPublicRoot { get; }

    public CanonicalModulePlan Plan { get; }

    public string BackendLoadViewRoot => Path.Combine(StagedRuntimeRoot, "backend-load-view");

    public RuntimeViewProjectionCommit BeginCommit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_commitStarted)
        {
            throw new InvalidOperationException("Runtime projection staging already started a commit.");
        }

        var commit = _owner.BeginCommit(this);
        _commitStarted = true;
        return commit;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RuntimeViewMaterializer.TryDeleteDirectory(StagingRoot);
    }
}

/// <summary>
/// runtime/public 的可回滚目录切换。Complete 后新投影成为最终结果；否则 Dispose 恢复旧投影。
/// </summary>
public sealed class RuntimeViewProjectionCommit : IDisposable
{
    private readonly string _runtimeRoot;
    private readonly string _publicRoot;
    private readonly string _runtimeBackupRoot;
    private readonly string _publicBackupRoot;
    private readonly string _commitWorkspaceRoot;
    private readonly bool _runtimePreviouslyExisted;
    private readonly bool _publicPreviouslyExisted;
    private readonly bool _runtimeCommitted;
    private readonly bool _publicCommitted;
    private bool _completed;
    private bool _disposed;

    internal RuntimeViewProjectionCommit(
        string runtimeRoot,
        string publicRoot,
        string runtimeBackupRoot,
        string publicBackupRoot,
        string commitWorkspaceRoot,
        bool runtimePreviouslyExisted,
        bool publicPreviouslyExisted,
        bool runtimeCommitted,
        bool publicCommitted)
    {
        _runtimeRoot = runtimeRoot;
        _publicRoot = publicRoot;
        _runtimeBackupRoot = runtimeBackupRoot;
        _publicBackupRoot = publicBackupRoot;
        _commitWorkspaceRoot = commitWorkspaceRoot;
        _runtimePreviouslyExisted = runtimePreviouslyExisted;
        _publicPreviouslyExisted = publicPreviouslyExisted;
        _runtimeCommitted = runtimeCommitted;
        _publicCommitted = publicCommitted;
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            return;
        }

        // active state 已原子提交后不能再回滚投影；先标记完成，再清理只用于恢复的备份。
        _completed = true;
        RuntimeViewMaterializer.TryDeleteDirectory(_runtimeBackupRoot);
        RuntimeViewMaterializer.TryDeleteDirectory(_publicBackupRoot);
        RuntimeViewMaterializer.TryDeleteDirectory(_commitWorkspaceRoot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_completed)
        {
            RuntimeViewMaterializer.RestoreProjection(
                _runtimeRoot,
                _runtimeBackupRoot,
                _runtimePreviouslyExisted,
                _runtimeCommitted);
            RuntimeViewMaterializer.RestoreProjection(
                _publicRoot,
                _publicBackupRoot,
                _publicPreviouslyExisted,
                _publicCommitted);
        }

        RuntimeViewMaterializer.TryDeleteDirectory(_commitWorkspaceRoot);
    }
}
