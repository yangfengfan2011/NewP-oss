namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件清单数据
/// </summary>
public class PluginManifest
{
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntryAssembly { get; set; } = string.Empty;
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
    public HostVersionRequirement? HostVersion { get; set; }
    public string? EntryAssemblySha256 { get; set; }
}

/// <summary>
/// 主机版本要求
/// </summary>
public class HostVersionRequirement
{
    public string? Min { get; set; }
    public string? Max { get; set; }
}
