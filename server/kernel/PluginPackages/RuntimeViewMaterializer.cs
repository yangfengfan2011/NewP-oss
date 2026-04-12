using ICP.Platform.Kernel.PluginDiscovery;
using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

public class RuntimeViewMaterializer
{
    private static readonly System.Text.Json.JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly InstalledPluginStateStore _stateStore;
    private readonly FrontendRuntimeManifestBuilder _frontendManifestBuilder;
    private readonly Action<string, string> _moveDirectory;

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore)
        : this(stateStore, new FrontendRuntimeManifestBuilder())
    {
    }

    public RuntimeViewMaterializer(InstalledPluginStateStore stateStore, FrontendRuntimeManifestBuilder frontendManifestBuilder)
        : this(stateStore, frontendManifestBuilder, Directory.Move)
    {
    }

    internal RuntimeViewMaterializer(
        InstalledPluginStateStore stateStore,
        FrontendRuntimeManifestBuilder frontendManifestBuilder,
        Action<string, string> moveDirectory)
    {
        _stateStore = stateStore;
        _frontendManifestBuilder = frontendManifestBuilder;
        _moveDirectory = moveDirectory;
    }

    public void Materialize(string installRoot)
    {
        var enabledRecords = _stateStore.LoadAllEnabled();
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

        var targetResourcesRoot = Path.Combine(runtimeRoot, "resources");
        CopyDirectory(packageResourcesRoot, targetResourcesRoot);
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
