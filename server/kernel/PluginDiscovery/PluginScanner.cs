using System.Text.Json;

namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件扫描器
/// </summary>
public class PluginScanner
{
    private const string ManifestFileName = "plugin.json";

    public PluginScanResult ScanPlugin(string pluginPath)
    {
        // 检查目录是否存在
        if (!Directory.Exists(pluginPath))
        {
            return PluginScanResult.Failure(
                PluginScanErrorType.DirectoryNotFound,
                $"Plugin directory not found: {pluginPath}");
        }

        // 检查清单文件是否存在
        var manifestPath = Path.Combine(pluginPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return PluginScanResult.Failure(
                PluginScanErrorType.ManifestNotFound,
                $"Plugin manifest not found: {manifestPath}");
        }

        // 解析清单
        PluginManifest manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Manifest deserialization returned null");
        }
        catch (Exception ex)
        {
            return PluginScanResult.Failure(
                PluginScanErrorType.InvalidManifest,
                $"Failed to parse manifest: {ex.Message}");
        }

        // 检查入口程序集是否存在
        var entryAssemblyPath = Path.Combine(pluginPath, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            return PluginScanResult.Failure(
                PluginScanErrorType.EntryAssemblyNotFound,
                $"Entry assembly not found: {entryAssemblyPath}");
        }

        return PluginScanResult.Success(manifest, pluginPath, entryAssemblyPath);
    }
}
