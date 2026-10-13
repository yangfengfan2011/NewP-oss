namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool ToClient { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
}
