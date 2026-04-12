namespace ICP.Platform.Modules.MetadataConfigCenter.DTOs;

public class SaveSettingDefinitionsRequest
{
    public List<SettingDefinitionDto> Definitions { get; set; } = [];
}

public class SaveSettingDefinitionsResult
{
    public List<SettingDefinitionDto> Definitions { get; set; } = [];

    public string Status { get; set; } = "已保存未应用";
}
