using ICP.Platform.Modules.MetadataConfigCenter.DTOs;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public interface ISettingDefinitionApplyService
{
    Task<ApplySettingDefinitionsResult> ApplySettingDefinitionsAsync(string moduleId, CancellationToken cancellationToken = default);
}
