namespace ICP.Platform.Modules.MetadataConfigCenter.Models;

public class MetadataModuleManifest
{
    public string SchemaVersion { get; set; } = "1.0";

    public string ModuleId { get; set; } = string.Empty;

    public string MetadataRoot { get; set; } = "metadata";

    public Dictionary<string, MetadataTypeDescriptor> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
