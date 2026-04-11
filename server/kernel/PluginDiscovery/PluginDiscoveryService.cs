namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件发现服务
/// </summary>
public class PluginDiscoveryService
{
    private readonly PluginScanner _scanner = new();
    private readonly PluginCompatibilityValidator? _validator;
    private readonly PluginDependencyValidator _dependencyValidator = new();

    public PluginDiscoveryService()
    {
    }

    public PluginDiscoveryService(string hostVersion, IEnumerable<string>? disabledPluginIds = null)
    {
        _validator = new PluginCompatibilityValidator(hostVersion, disabledPluginIds);
    }

    public IReadOnlyList<PluginScanResult> DiscoverPlugins(string pluginsRootPath)
    {
        // 如果根目录不存在，返回空列表而不是失败
        if (!Directory.Exists(pluginsRootPath))
        {
            return Array.Empty<PluginScanResult>();
        }

        var results = new List<PluginScanResult>();

        // 扫描所有子目录
        var pluginDirectories = Directory.GetDirectories(pluginsRootPath);
        foreach (var pluginDir in pluginDirectories)
        {
            var result = _scanner.ScanPlugin(pluginDir);

            // 如果扫描成功且配置了验证器，检查兼容性
            if (result.IsSuccess && _validator != null && result.Manifest != null)
            {
                var validationResult = _validator.Validate(result.Manifest, result.ResolvedEntryAssemblyPath);
                if (!validationResult.IsValid)
                {
                    // 将成功的扫描结果转换为不兼容错误，但保留清单信息
                    result = new PluginScanResult
                    {
                        IsSuccess = false,
                        Manifest = result.Manifest,
                        ErrorType = validationResult.ErrorType,
                        ErrorMessage = string.Join("; ", validationResult.Errors),
                        PluginDirectory = result.PluginDirectory,
                        ResolvedEntryAssemblyPath = result.ResolvedEntryAssemblyPath
                    };
                }
            }

            results.Add(result);
        }

        var governanceFailures = _dependencyValidator.Validate(results);

        return results
            .Select(result =>
            {
                var moduleId = result.Manifest?.ModuleId;
                if (result.IsSuccess
                    && !string.IsNullOrWhiteSpace(moduleId)
                    && governanceFailures.TryGetValue(moduleId, out var governanceFailure))
                {
                    return governanceFailure;
                }

                return result;
            })
            .ToList();
    }
}
