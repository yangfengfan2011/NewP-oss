namespace ICP.Platform.Modules.MetadataConfigCenter.Models;

public class SettingDefinition
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string ValueType { get; set; } = string.Empty;

    public string DefaultValue { get; set; } = string.Empty;

    public bool Required { get; set; }

    public bool IsSensitive { get; set; }

    public bool SupportsOverride { get; set; }

    public string Group { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;

    public string VersionSummary { get; set; } = string.Empty;
}
