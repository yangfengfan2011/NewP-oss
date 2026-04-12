using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Models;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public class SettingDefinitionAuthoringService : ISettingDefinitionAuthoringService
{
    private readonly IMetadataManifestService _metadataManifestService;
    private readonly ModuleMetadataStore _moduleMetadataStore;
    private readonly SettingDefinitionAuthoringAdapter _adapter;

    public SettingDefinitionAuthoringService(
        IMetadataManifestService metadataManifestService,
        ModuleMetadataStore moduleMetadataStore,
        SettingDefinitionAuthoringAdapter adapter)
    {
        _metadataManifestService = metadataManifestService;
        _moduleMetadataStore = moduleMetadataStore;
        _adapter = adapter;
    }

    public async Task<List<SettingDefinitionDto>> GetSettingDefinitionsAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var settingsSourcePath = await GetSettingsSourcePathAsync(moduleId, cancellationToken);
        var files = _moduleMetadataStore.EnumerateMetadataFiles(moduleId, settingsSourcePath, "*.json");

        var definitions = new List<SettingDefinition>();
        foreach (var file in files)
        {
            var content = await _moduleMetadataStore.ReadTextAsync(file, cancellationToken);
            definitions.AddRange(_adapter.ParseDefinitions(Path.GetFileName(file), content));
        }

        _adapter.Validate(definitions);
        return definitions.Select(MapToDto).ToList();
    }

    public async Task<SaveSettingDefinitionsResult> SaveSettingDefinitionsAsync(
        string moduleId,
        SaveSettingDefinitionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var settingsSourcePath = await GetSettingsSourcePathAsync(moduleId, cancellationToken);
        var definitions = request.Definitions.Select(MapToModel).ToList();

        _adapter.Validate(definitions);

        var generatedFiles = _adapter.BuildFileContents(moduleId, definitions);
        foreach (var generatedFile in generatedFiles)
        {
            var filePath = _moduleMetadataStore.ResolveMetadataFilePath(moduleId, settingsSourcePath, generatedFile.SourceFile);
            await _moduleMetadataStore.WriteTextAtomicallyAsync(filePath, generatedFile.Content, cancellationToken);
        }

        return new SaveSettingDefinitionsResult
        {
            Definitions = definitions.Select(MapToDto).ToList(),
            Status = "已保存未应用"
        };
    }

    private async Task<string> GetSettingsSourcePathAsync(string moduleId, CancellationToken cancellationToken)
    {
        var manifests = await _metadataManifestService.GetModuleManifestsAsync(cancellationToken);
        var manifest = manifests.SingleOrDefault(item => string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
        if (manifest is null || !manifest.Types.TryGetValue("settings", out var settingsDescriptor) || !settingsDescriptor.Enabled)
        {
            throw new InvalidOperationException($"Module {moduleId} does not expose editable settings metadata.");
        }

        return settingsDescriptor.SourcePath;
    }

    private static SettingDefinitionDto MapToDto(SettingDefinition definition)
    {
        return new SettingDefinitionDto
        {
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ValueType = definition.ValueType,
            DefaultValue = definition.DefaultValue,
            Required = definition.Required,
            IsSensitive = definition.IsSensitive,
            SupportsOverride = definition.SupportsOverride,
            Group = definition.Group,
            SourceFile = definition.SourceFile
        };
    }

    private static SettingDefinition MapToModel(SettingDefinitionDto definition)
    {
        return new SettingDefinition
        {
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ValueType = definition.ValueType,
            DefaultValue = definition.DefaultValue,
            Required = definition.Required,
            IsSensitive = definition.IsSensitive,
            SupportsOverride = definition.SupportsOverride,
            Group = definition.Group,
            SourceFile = definition.SourceFile
        };
    }
}
