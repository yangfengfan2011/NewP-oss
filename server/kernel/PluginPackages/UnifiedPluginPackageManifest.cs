namespace ICP.Platform.Kernel.PluginPackages;

public class UnifiedPluginPackageManifest
{
    public string PluginId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public UnifiedPluginPackageDistribution Distribution { get; set; } = new();
    public UnifiedPluginPackageHostVersion HostVersion { get; set; } = new();
    public UnifiedPluginPackageArtifacts Artifacts { get; set; } = new();
    public UnifiedPluginPackageChecksums Checksums { get; set; } = new();
}

public class UnifiedPluginPackageDistribution
{
    public string Channel { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
}

public class UnifiedPluginPackageHostVersion
{
    public string Min { get; set; } = string.Empty;
    public string Max { get; set; } = string.Empty;
}

public class UnifiedPluginPackageArtifacts
{
    public string Backend { get; set; } = string.Empty;
    public string Frontend { get; set; } = string.Empty;
}

public class UnifiedPluginPackageChecksums
{
    public string Sha256Manifest { get; set; } = string.Empty;
}
