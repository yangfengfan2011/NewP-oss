namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportRequest
{
    public required string XmlContent { get; set; }
    public required string SourceFileName { get; set; }
    public required string ModuleId { get; set; }
    public string? Environment { get; set; }
    public string? TenantId { get; set; }
}
