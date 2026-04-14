using System.Text.Json;
using ICP.Platform.Kernel.PluginDiscovery;

namespace ICP.Platform.Kernel.PluginPackages;

public class UnifiedPluginPackageScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UnifiedPluginPackageScanResult ScanPackage(string packageRoot)
    {
        // 扫描阶段只做结构校验和路径收口，不触发任何代码执行。
        // 目标是尽早拒绝缺失、越界或无法解析的包，避免把坏包带入安装流程。
        if (!Directory.Exists(packageRoot))
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.PackageRootNotFound,
                $"Package root not found: {packageRoot}");
        }

        var pluginJsonPath = Path.Combine(packageRoot, "plugin.json");
        if (!File.Exists(pluginJsonPath))
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ManifestNotFound,
                $"plugin.json not found at {pluginJsonPath}");
        }

        UnifiedPluginPackageManifest manifest;
        try
        {
            var json = File.ReadAllText(pluginJsonPath);
            manifest = JsonSerializer.Deserialize<UnifiedPluginPackageManifest>(json, JsonOptions)
                ?? throw new JsonException("plugin.json deserialized to null");
        }
        catch (Exception ex)
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ManifestParseError,
                $"Failed to parse plugin.json: {ex.Message}");
        }

        try
        {
            // 子清单是包能否安全落地的关键边界；任意一层路径越界或缺失，整个包都应判定为不可安装。
            var backendPath = ResolveContainedFilePath(packageRoot, manifest.Artifacts.Backend, "Artifacts.Backend");
            var frontendPath = ResolveContainedFilePath(packageRoot, manifest.Artifacts.Frontend, "Artifacts.Frontend");

            ValidateBackendManifest(backendPath);
            ValidateFrontendManifest(frontendPath);

            return UnifiedPluginPackageScanResult.Success(
                packageRoot,
                manifest,
                backendPath,
                frontendPath);
        }
        catch (JsonException ex)
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ManifestParseError,
                ex.Message);
        }
        catch (IOException ex)
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ArtifactNotFound,
                ex.Message);
        }
    }

    private static void ValidateBackendManifest(string backendManifestPath)
    {
        PluginManifest backendManifest;
        try
        {
            backendManifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(backendManifestPath), JsonOptions)
                ?? throw new JsonException($"Backend manifest '{backendManifestPath}' deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new JsonException($"Failed to parse child manifest '{backendManifestPath}': {ex.Message}", ex);
        }

        ResolveContainedFilePath(Path.GetDirectoryName(backendManifestPath)!, backendManifest.EntryAssembly, "EntryAssembly");
    }

    private static void ValidateFrontendManifest(string frontendManifestPath)
    {
        FrontendPackageManifest frontendManifest;
        try
        {
            frontendManifest = JsonSerializer.Deserialize<FrontendPackageManifest>(File.ReadAllText(frontendManifestPath), JsonOptions)
                ?? throw new JsonException($"Frontend manifest '{frontendManifestPath}' deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new JsonException($"Failed to parse child manifest '{frontendManifestPath}': {ex.Message}", ex);
        }

        var frontendRoot = Path.GetDirectoryName(frontendManifestPath)!;
        var entryManifestPath = ResolveContainedFilePath(frontendRoot, frontendManifest.EntryManifest, "EntryManifest");
        ResolveContainedDirectoryPath(frontendRoot, frontendManifest.AssetRoot, "AssetRoot");

        FrontendEntryManifest entryManifest;
        try
        {
            entryManifest = JsonSerializer.Deserialize<FrontendEntryManifest>(File.ReadAllText(entryManifestPath), JsonOptions)
                ?? throw new JsonException($"Frontend runtime manifest '{entryManifestPath}' deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new JsonException($"Failed to parse child manifest '{entryManifestPath}': {ex.Message}", ex);
        }

        ResolveContainedFilePath(frontendRoot, entryManifest.Entry, "Entry");

        foreach (var cssPath in entryManifest.Css)
        {
            ResolveContainedFilePath(frontendRoot, cssPath, "Css");
        }
    }

    private static string ResolveContainedFilePath(string rootPath, string? relativePath, string propertyName)
    {
        var candidatePath = ResolveContainedPath(rootPath, relativePath, propertyName);
        if (!File.Exists(candidatePath))
        {
            throw new IOException($"Required file not found for {propertyName}: {relativePath}");
        }

        return candidatePath;
    }

    private static string ResolveContainedDirectoryPath(string rootPath, string? relativePath, string propertyName)
    {
        var candidatePath = ResolveContainedPath(rootPath, relativePath, propertyName);
        if (!Directory.Exists(candidatePath))
        {
            throw new IOException($"Required directory not found for {propertyName}: {relativePath}");
        }

        return candidatePath;
    }

    private static string ResolveContainedPath(string rootPath, string? relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new IOException($"Manifest property {propertyName} must not be empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new IOException($"Manifest property {propertyName} resolves to an unsafe path.");
        }

        // 所有相对路径都必须留在包根内，确保包可搬移且不会通过 .. 逃逸到外部目录。
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        if (!fullCandidatePath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullCandidatePath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Manifest property {propertyName} resolves to an unsafe path.");
        }

        return fullCandidatePath;
    }

    private sealed class FrontendPackageManifest
    {
        public string EntryManifest { get; set; } = string.Empty;
        public string AssetRoot { get; set; } = string.Empty;
    }

    private sealed class FrontendEntryManifest
    {
        public string Entry { get; set; } = string.Empty;
        public List<string> Css { get; set; } = new();
    }
}
