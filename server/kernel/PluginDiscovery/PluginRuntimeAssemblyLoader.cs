using System.Reflection;

namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 将插件发现结果转换为可供宿主装配的程序集集合。
/// </summary>
public class PluginRuntimeAssemblyLoader
{
    private readonly PluginDiscoveryService _discoveryService;

    public PluginRuntimeAssemblyLoader(string hostVersion, IEnumerable<string>? disabledPluginIds = null)
    {
        _discoveryService = new PluginDiscoveryService(hostVersion, disabledPluginIds);
    }

    public PluginRuntimeAssemblyLoadResult LoadAssemblies(string pluginsRootPath)
    {
        var discoveryResults = _discoveryService.DiscoverPlugins(pluginsRootPath);
        var loadedAssemblies = new List<Assembly>();
        var successfulPlugins = new List<PluginScanResult>();
        var failedPlugins = new List<PluginScanResult>();

        foreach (var discoveryResult in discoveryResults)
        {
            if (!discoveryResult.IsSuccess || string.IsNullOrWhiteSpace(discoveryResult.ResolvedEntryAssemblyPath))
            {
                failedPlugins.Add(discoveryResult);
                continue;
            }

            try
            {
                var assembly = Assembly.LoadFrom(discoveryResult.ResolvedEntryAssemblyPath);
                loadedAssemblies.Add(assembly);
                successfulPlugins.Add(discoveryResult);
            }
            catch (Exception ex)
            {
                failedPlugins.Add(new PluginScanResult
                {
                    IsSuccess = false,
                    Manifest = discoveryResult.Manifest,
                    ErrorType = PluginScanErrorType.EntryAssemblyLoadFailed,
                    ErrorMessage = $"Failed to load entry assembly '{discoveryResult.ResolvedEntryAssemblyPath}': {ex.Message}",
                    PluginDirectory = discoveryResult.PluginDirectory,
                    ResolvedEntryAssemblyPath = discoveryResult.ResolvedEntryAssemblyPath
                });
            }
        }

        return new PluginRuntimeAssemblyLoadResult
        {
            LoadedAssemblies = loadedAssemblies,
            SuccessfulPlugins = successfulPlugins,
            FailedPlugins = failedPlugins
        };
    }
}

public class PluginRuntimeAssemblyLoadResult
{
    public IReadOnlyList<Assembly> LoadedAssemblies { get; init; } = Array.Empty<Assembly>();
    public IReadOnlyList<PluginScanResult> SuccessfulPlugins { get; init; } = Array.Empty<PluginScanResult>();
    public IReadOnlyList<PluginScanResult> FailedPlugins { get; init; } = Array.Empty<PluginScanResult>();
}
