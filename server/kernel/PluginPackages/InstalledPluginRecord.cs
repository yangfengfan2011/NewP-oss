namespace ICP.Platform.Kernel.PluginPackages;

public class InstalledPluginRecord
{
    public string ModuleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "Installed";
    public string PackageRoot { get; set; } = string.Empty;
    public string? CurrentEnabledVersion { get; set; }
    public bool IsDisabled { get; set; }
    public List<string> InstalledVersions { get; set; } = new();
}
