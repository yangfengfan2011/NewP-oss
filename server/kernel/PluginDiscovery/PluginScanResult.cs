namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件扫描结果
/// </summary>
public class PluginScanResult
{
    public bool IsSuccess { get; set; }
    public PluginManifest? Manifest { get; set; }
    public PluginScanErrorType ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PluginDirectory { get; set; }
    public string? ResolvedEntryAssemblyPath { get; set; }

    public static PluginScanResult Success(PluginManifest manifest, string pluginDirectory, string resolvedEntryAssemblyPath)
    {
        return new PluginScanResult
        {
            IsSuccess = true,
            Manifest = manifest,
            PluginDirectory = pluginDirectory,
            ResolvedEntryAssemblyPath = resolvedEntryAssemblyPath
        };
    }

    public static PluginScanResult Failure(PluginScanErrorType errorType, string errorMessage)
    {
        return new PluginScanResult
        {
            IsSuccess = false,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// 插件扫描错误类型
/// </summary>
public enum PluginScanErrorType
{
    None,
    DirectoryNotFound,
    ManifestNotFound,
    InvalidManifest,
    EntryAssemblyNotFound,
    IncompatibleHostVersion,
    EntryAssemblyLoadFailed,
    PluginDisabled,
    EntryAssemblyHashMismatch,
    DependencyNotSatisfied,
    CircularDependency
}
