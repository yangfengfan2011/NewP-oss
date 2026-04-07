namespace ICP.Platform.Kernel.PluginPackages;

public enum UnifiedPluginPackageScanFailureType
{
    ManifestNotFound,
    ManifestParseError,
    ArtifactNotFound,
    PackageRootNotFound
}

public class UnifiedPluginPackageScanError
{
    public UnifiedPluginPackageScanFailureType Type { get; init; }
    public string Message { get; init; } = string.Empty;
}

public class UnifiedPluginPackageScanResult
{
    public bool IsSuccess { get; init; }
    public UnifiedPluginPackageManifest? Manifest { get; init; }
    public string PackageRoot { get; init; } = string.Empty;
    public string ResolvedBackendManifestPath { get; init; } = string.Empty;
    public string ResolvedFrontendManifestPath { get; init; } = string.Empty;
    public UnifiedPluginPackageScanError? Error { get; init; }

    public string ModuleId => Manifest?.ModuleId ?? string.Empty;
    public string BackendArtifactPath => Manifest?.Artifacts?.Backend ?? string.Empty;
    public string FrontendArtifactPath => Manifest?.Artifacts?.Frontend ?? string.Empty;
    public UnifiedPluginPackageScanFailureType? FailureType => Error?.Type;
    public string ErrorMessage => Error?.Message ?? string.Empty;

    public static UnifiedPluginPackageScanResult Success(
        string packageRoot,
        UnifiedPluginPackageManifest manifest,
        string resolvedBackendManifestPath,
        string resolvedFrontendManifestPath) =>
        new()
        {
            IsSuccess = true,
            Manifest = manifest,
            PackageRoot = packageRoot,
            ResolvedBackendManifestPath = resolvedBackendManifestPath,
            ResolvedFrontendManifestPath = resolvedFrontendManifestPath
        };

    public static UnifiedPluginPackageScanResult Failure(
        string packageRoot,
        UnifiedPluginPackageScanFailureType type,
        string message) =>
        new()
        {
            IsSuccess = false,
            PackageRoot = packageRoot,
            Error = new UnifiedPluginPackageScanError
            {
                Type = type,
                Message = message
            }
        };
}
