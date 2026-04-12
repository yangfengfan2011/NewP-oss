using ICP.Platform.Modules.MetadataConfigCenter.DTOs;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public interface ISettingDefinitionAuthoringService
{
    Task<List<SettingDefinitionDto>> GetSettingDefinitionsAsync(string moduleId, CancellationToken cancellationToken = default);

    Task<SaveSettingDefinitionsResult> SaveSettingDefinitionsAsync(
        string moduleId,
        SaveSettingDefinitionsRequest request,
        CancellationToken cancellationToken = default);
}
