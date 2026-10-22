namespace ICP.Platform.Modules.MetadataConfigCenter.DTOs;

public class SaveSettingDefinitionsRequest
{
    public List<SettingDefinitionDto> Definitions { get; set; } = [];
}

public class SaveSettingDefinitionsResult
{
    public List<SettingDefinitionDto> Definitions { get; set; } = [];

    public string Status { get; set; } = "已保存未应用";

    public bool HasUnsavedChanges { get; set; }

    public bool HasUnappliedChanges { get; set; } = true;

    public bool GitSyncPending { get; set; } = true;
}

public class ApplySettingDefinitionsResult
{
    public string Status { get; set; } = "当前没有待处理变更";

    public bool HasUnappliedChanges { get; set; }
}
