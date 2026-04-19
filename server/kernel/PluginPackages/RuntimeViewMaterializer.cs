using ICP.Platform.Kernel.PluginDiscovery;
using System.Text.Json;

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
    private readonly Action<string, string> _moveDirectory;

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore)
        : this(stateStore, new FrontendRuntimeManifestBuilder())
    {
    }

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore, FrontendRuntimeManifestBuilder frontendManifestBuilder)
        : this(stateStore, frontendManifestBuilder, new LocalizationRuntimeManifestBuilder(), Directory.Move)
    {
    }

    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        Action<string, string> moveDirectory)
        : this(stateStore, frontendManifestBuilder, new LocalizationRuntimeManifestBuilder(), moveDirectory)
    {
    }

    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        LocalizationRuntimeManifestBuilder localizationManifestBuilder,
        Action<string, string> moveDirectory)
    {
        _stateStore = stateStore;
        _frontendManifestBuilder = frontendManifestBuilder;
        _localizationManifestBuilder = localizationManifestBuilder;
        _moveDirectory = moveDirectory;
    }

    public void Materialize(string installRoot)
    {
        var enabledRecords = _stateStore.LoadAllEnabled();
        // 所有输出先进入 staging；只有全部成功后才会替换正式 runtime/public，
        // 避免中途失败时留下半更新的运行时视图。
        var stagingRoot = Path.Combine(installRoot, $".materialize-staging-{Guid.NewGuid():N}");
        var stagedRuntimeRoot = Path.Combine(stagingRoot, "runtime");
        var stagedBackendViewRoot = Path.Combine(stagedRuntimeRoot, "backend-load-view");
        var stagedPublicRoot = Path.Combine(stagingRoot, "public");
        var commitWorkspaceRoot = Path.Combine(installRoot, $".materialize-commit-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagedBackendViewRoot);
            Directory.CreateDirectory(stagedPublicRoot);

            foreach (var record in enabledRecords)
            {
                MaterializeBackendView(stagedBackendViewRoot, record);
                StageFrontendAssets(stagedPublicRoot, record);
                StageModuleMetadata(stagedRuntimeRoot, record);
                StagePlatformResources(stagedRuntimeRoot, record);
            }

            var frontendManifestPath = Path.Combine(stagedRuntimeRoot, "frontend-plugin-manifest.json");
            File.WriteAllText(frontendManifestPath, _frontendManifestBuilder.Build(enabledRecords));

            var enabledModuleOrder = enabledRecords
                .Select(record => record.ModuleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var localizationManifestPath = Path.Combine(stagedRuntimeRoot, "localization-manifest.json");
            File.WriteAllText(localizationManifestPath, _localizationManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder));

            ReplaceProjection(installRoot, stagedRuntimeRoot, stagedPublicRoot, commitWorkspaceRoot);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            if (Directory.Exists(commitWorkspaceRoot))
            {
                Directory.Delete(commitWorkspaceRoot, recursive: true);
            }
        }
    }

    private static void MaterializeBackendView(string backendViewRoot, InstalledPluginRecord record)
    {
        var backendRoot = Path.Combine(record.PackageRoot, "backend");
        var backendManifestPath = Path.Combine(backendRoot, "plugin.backend.json");
        var backendManifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(backendManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Backend manifest '{backendManifestPath}' deserialized to null.");

        var targetDirectory = Path.Combine(backendViewRoot, record.ModuleId);
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

    private static void StageFrontendAssets(string publicRoot, InstalledPluginRecord record)
    {
        var frontendRoot = Path.GetFullPath(Path.Combine(record.PackageRoot, "frontend"));
        var frontendManifestPath = Path.Combine(frontendRoot, "plugin.frontend.json");
        var frontendManifest = System.Text.Json.JsonSerializer.Deserialize<FrontendPackageManifest>(File.ReadAllText(frontendManifestPath), ManifestJsonOptions)
            ?? throw new System.Text.Json.JsonException($"Frontend manifest '{frontendManifestPath}' deserialized to null.");

        var assetRootPath = ResolveContainedDirectoryPath(frontendRoot, frontendManifest.AssetRoot, "AssetRoot");
        var entryManifestPath = ResolveContainedFilePath(frontendRoot, frontendManifest.EntryManifest, "EntryManifest");
        var entryManifest = System.Text.Json.JsonSerializer.Deserialize<FrontendEntryManifest>(File.ReadAllText(entryManifestPath), ManifestJsonOptions)
            ?? throw new System.Text.Json.JsonException($"Frontend runtime manifest '{entryManifestPath}' deserialized to null.");
        // 这里显式验证 entry 文件存在，但不直接复制 entry 本身；
        // public 目录交付的是资产根和入口清单，真正入口仍由运行时 manifest 决定。
        ResolveContainedFilePath(frontendRoot, entryManifest.Entry, "Entry");

        var targetRoot = Path.Combine(publicRoot, record.ModuleId, record.Version);
        var assetRootName = Path.GetFileName(assetRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        CopyDirectory(assetRootPath, Path.Combine(targetRoot, assetRootName));
        File.Copy(
            entryManifestPath,
            Path.Combine(targetRoot, Path.GetFileName(entryManifestPath)),
            overwrite: true);
    }

    private static void StageModuleMetadata(string runtimeRoot, InstalledPluginRecord record)
    {
        var packageMetadataRoot = Path.Combine(record.PackageRoot, "modules", record.ModuleId, "metadata");
        if (!Directory.Exists(packageMetadataRoot))
        {
            return;
        }

        var targetMetadataRoot = Path.Combine(runtimeRoot, "modules", record.ModuleId, "metadata");
        CopyDirectory(packageMetadataRoot, targetMetadataRoot);
    }

    private static void StagePlatformResources(string runtimeRoot, InstalledPluginRecord record)
    {
        var packageResourcesRoot = Path.Combine(record.PackageRoot, "resources");
        if (!Directory.Exists(packageResourcesRoot))
        {
            return;
        }

        // 平台公共资源按最终生效视图汇总到同一个 resources 目录；
        // 冲突时直接失败，而不是依赖复制顺序做隐式覆盖。
        var targetResourcesRoot = Path.Combine(runtimeRoot, "resources");
        CopyDirectoryRejectingConflicts(packageResourcesRoot, targetResourcesRoot, record.ModuleId);
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

    private void ReplaceProjection(string installRoot, string stagedRuntimeRoot, string stagedPublicRoot, string commitWorkspaceRoot)
    {
        var runtimeRoot = Path.Combine(installRoot, "runtime");
        var publicRoot = Path.Combine(installRoot, "public");
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

            _moveDirectory(stagedRuntimeRoot, runtimeRoot);
            runtimeCommitted = true;

            _moveDirectory(stagedPublicRoot, publicRoot);
            publicCommitted = true;

            if (Directory.Exists(runtimeBackupRoot))
            {
                Directory.Delete(runtimeBackupRoot, recursive: true);
            }

            if (Directory.Exists(publicBackupRoot))
            {
                Directory.Delete(publicBackupRoot, recursive: true);
            }
        }
        catch
        {
            RestoreProjection(runtimeRoot, runtimeBackupRoot, runtimeExists, runtimeCommitted);
            RestoreProjection(publicRoot, publicBackupRoot, publicExists, publicCommitted);
            throw;
        }
    }

    private static void RestoreProjection(string destinationPath, string backupPath, bool destinationPreviouslyExisted, bool destinationWasReplaced)
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
