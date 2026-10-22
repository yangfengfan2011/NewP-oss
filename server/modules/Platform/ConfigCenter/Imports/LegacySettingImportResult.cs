namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportResult
{
    public int TotalCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
