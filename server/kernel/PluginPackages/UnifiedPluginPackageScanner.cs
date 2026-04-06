using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

public class UnifiedPluginPackageScanner
{
    public UnifiedPluginPackageScanResult ScanPackage(string packageRoot)
    {
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
            manifest = JsonSerializer.Deserialize<UnifiedPluginPackageManifest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
                ?? throw new JsonException("plugin.json deserialized to null");
        }
        catch (Exception ex)
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ManifestParseError,
                $"Failed to parse plugin.json: {ex.Message}");
        }

        var backendPath = Path.GetFullPath(Path.Combine(packageRoot, manifest.Artifacts.Backend));
        var frontendPath = Path.GetFullPath(Path.Combine(packageRoot, manifest.Artifacts.Frontend));

        var missingPaths = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.Artifacts.Backend) || !File.Exists(backendPath))
        {
            missingPaths.Add(manifest.Artifacts.Backend);
        }

        if (string.IsNullOrWhiteSpace(manifest.Artifacts.Frontend) || !File.Exists(frontendPath))
        {
            missingPaths.Add(manifest.Artifacts.Frontend);
        }

        if (missingPaths.Any())
        {
            return UnifiedPluginPackageScanResult.Failure(
                packageRoot,
                UnifiedPluginPackageScanFailureType.ArtifactNotFound,
                $"Missing child manifest(s): {string.Join(", ", missingPaths)}");
        }

        return UnifiedPluginPackageScanResult.Success(
            packageRoot,
            manifest,
            backendPath,
            frontendPath);
    }
}
