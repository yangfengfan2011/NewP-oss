using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.ModulePlanning;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.Kernel.PluginPackages;

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
    private readonly Action<string, string> _moveDirectory;
    private readonly string? _builtInModulesRoot;
    private readonly ModuleCapsuleCandidateReader _candidateReader = new();
    private readonly CanonicalModulePlanCompiler _compiler = new();

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore)
        : this(stateStore, new FrontendRuntimeManifestBuilder())
    {
    }

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore, FrontendRuntimeManifestBuilder frontendManifestBuilder)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            Directory.Move)
    {
    }

    public RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        string builtInModulesRoot)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            Directory.Move,
            builtInModulesRoot)
    {
    }

    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        Action<string, string> moveDirectory)
        : this(
            stateStore,
            frontendManifestBuilder,
            new LocalizationRuntimeManifestBuilder(),
            new MetadataRuntimeManifestBuilder(),
            moveDirectory)
    {
    }

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
            null)
    {
    }

    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        LocalizationRuntimeManifestBuilder localizationManifestBuilder,
        MetadataRuntimeManifestBuilder metadataRuntimeManifestBuilder,
        Action<string, string> moveDirectory,
        string? builtInModulesRoot)
    {
        _stateStore = stateStore;
        _frontendManifestBuilder = frontendManifestBuilder;
        _localizationManifestBuilder = localizationManifestBuilder;
        _metadataRuntimeManifestBuilder = metadataRuntimeManifestBuilder;
        _moveDirectory = moveDirectory;
        _builtInModulesRoot = string.IsNullOrWhiteSpace(builtInModulesRoot)
            ? null
            : Path.GetFullPath(builtInModulesRoot);
    }

    public CanonicalModulePlan Materialize(string installRoot)
    {
        var enabledRecords = _stateStore.LoadAllEnabled();
        var installedCandidates = _candidateReader.ReadInstalled(enabledRecords);
        var candidates = installedCandidates.AsEnumerable();
        if (_builtInModulesRoot is not null)
        {
            var installedModuleIds = installedCandidates
                .Select(static candidate => candidate.ModuleId)
                .ToHashSet(StringComparer.Ordinal);
            // B11 之前由 compatibility resolver 选择同 ID 的已安装版本；编译器仍负责拒绝大小写折叠碰撞。
            candidates = candidates.Concat(
                _candidateReader.ReadBuiltIn(_builtInModulesRoot)
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
                MaterializeBackendView(stagedBackendViewRoot, module);
                StageFrontendAssets(stagedPublicRoot, module);
                StageModuleMetadata(stagedRuntimeRoot, module);
                StagePlatformResources(stagedRuntimeRoot, module);
            }

            var frontendManifestPath = Path.Combine(stagedRuntimeRoot, "frontend-plugin-manifest.json");
            File.WriteAllText(frontendManifestPath, _frontendManifestBuilder.Build(plan));

            var enabledModuleOrder = plan.Modules
                .Select(static module => module.ModuleId)
                .ToArray();
            var localizationManifestPath = Path.Combine(stagedRuntimeRoot, "localization-manifest.json");
            File.WriteAllText(
                localizationManifestPath,
                BindProjection(_localizationManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder), plan));

            var metadataRuntimeManifestPath = Path.Combine(stagedRuntimeRoot, "metadata-runtime-manifest.json");
            File.WriteAllText(
                metadataRuntimeManifestPath,
                BindProjection(_metadataRuntimeManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder), plan));

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

    private void MaterializeBackendView(string backendViewRoot, CanonicalModulePlanModule module)
    {
        if (string.IsNullOrWhiteSpace(module.BackendEntry))
        {
            return;
        }

        if (module.Provenance.SourceKind == "built-in")
        {
            MaterializeBuiltInBackendView(backendViewRoot, module);
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
        CanonicalModulePlanModule module)
    {
        var sourceManifestPath = Path.Combine(module.PackageRoot, "plugin.json");
        var distributionRoot = _builtInModulesRoot is null
            ? Path.GetDirectoryName(module.PackageRoot)!
            : Path.GetDirectoryName(_builtInModulesRoot)!;
        var entryAssemblyPath = Path.GetFullPath(Path.Combine(module.PackageRoot, module.BackendEntry!));
        if (!entryAssemblyPath.StartsWith(
                Path.GetFullPath(distributionRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(entryAssemblyPath))
        {
            throw new FileNotFoundException(
                $"Built-in backend entry for '{module.ModuleId}' is missing or outside the distribution root.",
                module.BackendEntry);
        }

        var targetDirectory = Path.Combine(backendViewRoot, module.ModuleId);
        Directory.CreateDirectory(targetDirectory);
        var targetEntryName = Path.GetFileName(entryAssemblyPath);
        File.Copy(entryAssemblyPath, Path.Combine(targetDirectory, targetEntryName), overwrite: true);

        var manifest = JsonNode.Parse(File.ReadAllText(sourceManifestPath))?.AsObject()
            ?? throw new JsonException($"Built-in module manifest '{sourceManifestPath}' deserialized to null.");
        manifest["entryAssembly"] = targetEntryName;
        File.WriteAllText(
            Path.Combine(targetDirectory, "plugin.json"),
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void StageFrontendAssets(string publicRoot, CanonicalModulePlanModule module)
    {
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

    private static void StageModuleMetadata(string runtimeRoot, CanonicalModulePlanModule module)
    {
        if (string.Equals(module.FrontendBindingMode, "standard-data", StringComparison.Ordinal))
        {
            StageStandardDataModule(runtimeRoot, module);
            return;
        }

        var packageMetadataRoot = module.Provenance.SourceKind == "built-in"
            ? Path.Combine(module.PackageRoot, "metadata")
            : Path.Combine(module.PackageRoot, "modules", module.ModuleId, "metadata");
        if (!Directory.Exists(packageMetadataRoot))
        {
            return;
        }

        var targetMetadataRoot = Path.Combine(runtimeRoot, "modules", module.ModuleId, "metadata");
        CopyDirectory(packageMetadataRoot, targetMetadataRoot);
    }

    private static void StageStandardDataModule(string runtimeRoot, CanonicalModulePlanModule module)
    {
        var moduleRuntimeRoot = Path.Combine(runtimeRoot, "modules", module.ModuleId);
        var targetMetadataRoot = Path.Combine(moduleRuntimeRoot, "metadata");
        var packageMetadataRoot = Path.Combine(module.PackageRoot, "metadata");
        if (Directory.Exists(packageMetadataRoot))
        {
            CopyDirectory(packageMetadataRoot, targetMetadataRoot);
        }

        var manifest = ReadPackageObject(module.PackageRoot, "plugin.json");
        var artifacts = manifest["artifacts"]?.AsObject()
            ?? throw new JsonException($"Standard data package '{module.ModuleId}' is missing artifacts.");
        var blueprintRelativePath = RequiredString(artifacts, "blueprint", module.ModuleId);
        var projectionRelativePath = RequiredString(artifacts, "standardProjection", module.ModuleId);
        var blueprintPath = ResolveContainedFilePath(module.PackageRoot, blueprintRelativePath, "Blueprint");
        var projectionPath = ResolveContainedFilePath(module.PackageRoot, projectionRelativePath, "StandardProjection");

        CopyContract(blueprintPath, Path.Combine(moduleRuntimeRoot, "authoring", "newp.plugin.json"));
        CopyContract(projectionPath, Path.Combine(moduleRuntimeRoot, "projections", "standard-data.json"));

        var blueprint = JsonNode.Parse(File.ReadAllText(blueprintPath))?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{module.ModuleId}' deserialized to null.");
        var projection = JsonNode.Parse(File.ReadAllText(projectionPath))?.AsObject()
            ?? throw new JsonException($"Standard data projection for '{module.ModuleId}' deserialized to null.");
        WriteStandardDataMetadata(targetMetadataRoot, module, blueprint, projection);
    }

    private static void WriteStandardDataMetadata(
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
            var permissionCodes = resourceProjection["permissionCodes"]?.AsArray()
                .Select(static item => item?.GetValue<string>() ?? string.Empty)
                .Where(static code => code.Length > 0)
                .ToArray() ?? [];
            var viewPermission = permissionCodes.Single(code => code.EndsWith(".View", StringComparison.Ordinal));
            var createPermission = permissionCodes.Single(code => code.EndsWith(".Create", StringComparison.Ordinal));
            var updatePermission = permissionCodes.Single(code => code.EndsWith(".Update", StringComparison.Ordinal));
            var deletePermission = permissionCodes.Single(code => code.EndsWith(".Delete", StringComparison.Ordinal));
            var listViewKey = $"{moduleSlug}.{resourceSlug}.list";
            var detailViewKey = $"{moduleSlug}.{resourceSlug}.detail";
            var fields = resource["fields"]?.AsArray()
                ?? throw new JsonException($"Standard data resource '{module.ModuleId}.{resourceKey}' is missing fields.");

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

            WriteJson(
                Path.Combine(metadataRoot, "views", $"{resourceKey}List.view.json"),
                new
                {
                    schemaVersion = "1.0",
                    type = "view",
                    view = new
                    {
                        key = listViewKey,
                        dicKey = $"{resourceKey}ListView",
                        titleKey = RequiredString(resource, "displayName", module.ModuleId),
                        mode = "list",
                        model = resourceKey,
                        editorMode = "drawer",
                        detailViewKey,
                        columns,
                        pageActions = new object[]
                        {
                            new { key = "create", labelKey = "Action_Create", standardAction = "create", variant = "primary", permissions = new[] { createPermission } },
                            new { key = "refresh", labelKey = "Action_Refresh", standardAction = "refresh", variant = (string?)null, permissions = Array.Empty<string>() }
                        },
                        rowActions = new object[]
                        {
                            new { key = "view", labelKey = "Action_View", standardAction = "view", variant = (string?)null, permissions = Array.Empty<string>() },
                            new { key = "edit", labelKey = "Action_Edit", standardAction = "edit", variant = (string?)null, permissions = new[] { updatePermission } },
                            new { key = "delete", labelKey = "Action_Delete", standardAction = "delete", variant = "danger", permissions = new[] { deletePermission } }
                        }
                    }
                });
            WriteJson(
                Path.Combine(metadataRoot, "views", $"{resourceKey}Detail.view.json"),
                new
                {
                    schemaVersion = "1.0",
                    type = "view",
                    view = new
                    {
                        key = detailViewKey,
                        dicKey = $"{resourceKey}DetailView",
                        titleKey = RequiredString(resource, "displayName", module.ModuleId),
                        mode = "detail",
                        model = resourceKey,
                        pageActions = new[]
                        {
                            new { key = "edit", labelKey = "Action_Edit", standardAction = "edit", permissions = new[] { updatePermission } }
                        }
                    }
                });
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

    private static JsonObject ReadPackageObject(string packageRoot, string relativePath)
    {
        var path = ResolveContainedFilePath(packageRoot, relativePath, "PackageManifest");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new JsonException($"Package file '{relativePath}' deserialized to null.");
    }

    private static string RequiredString(JsonObject value, string propertyName, string moduleId) =>
        value[propertyName]?.GetValue<string>() is { Length: > 0 } result
            ? result
            : throw new JsonException($"Standard data package '{moduleId}' is missing {propertyName}.");

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

    private static void StagePlatformResources(string runtimeRoot, CanonicalModulePlanModule module)
    {
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
        if (!fullCandidatePath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullCandidatePath, fullRootPath, StringComparison.OrdinalIgnoreCase))
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
