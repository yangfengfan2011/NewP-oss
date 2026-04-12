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
            var versionSummary = _moduleMetadataStore.ComputeVersionSummary(content);
            var parsedDefinitions = _adapter.ParseDefinitions(Path.GetFileName(file), content);
            foreach (var definition in parsedDefinitions)
            {
                definition.VersionSummary = versionSummary;
            }

            definitions.AddRange(parsedDefinitions);
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
        var gitSyncPending = false;
        var hasUnappliedChanges = false;

        foreach (var generatedFile in generatedFiles)
        {
            var filePath = _moduleMetadataStore.ResolveAuthoringMetadataFilePath(moduleId, settingsSourcePath, generatedFile.SourceFile);
            if (!await FileContentMatchesAsync(filePath, generatedFile.Content, cancellationToken))
            {
                gitSyncPending = true;
            }

            var runtimeFilePath = _moduleMetadataStore.ResolveRuntimeMetadataFilePath(moduleId, settingsSourcePath, generatedFile.SourceFile);
            if (string.IsNullOrWhiteSpace(runtimeFilePath)
                || !await FileContentMatchesAsync(runtimeFilePath, generatedFile.Content, cancellationToken))
            {
                hasUnappliedChanges = true;
            }

            await _moduleMetadataStore.WriteTextAtomicallyAsync(filePath, generatedFile.Content, cancellationToken);
        }

        return new SaveSettingDefinitionsResult
        {
            Definitions = definitions.Select(MapToDto).ToList(),
            Status = BuildStatus(gitSyncPending, hasUnappliedChanges),
            HasUnsavedChanges = false,
            HasUnappliedChanges = hasUnappliedChanges,
            GitSyncPending = gitSyncPending
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
            SourceFile = definition.SourceFile,
            VersionSummary = definition.VersionSummary
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
            SourceFile = definition.SourceFile,
            VersionSummary = definition.VersionSummary
        };
    }

    private async Task<bool> FileContentMatchesAsync(string filePath, string expectedContent, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var actualContent = await _moduleMetadataStore.ReadTextAsync(filePath, cancellationToken);
        return NormalizeLineEndings(actualContent).Equals(NormalizeLineEndings(expectedContent), StringComparison.Ordinal);
    }

    private static string BuildStatus(bool gitSyncPending, bool hasUnappliedChanges)
    {
        if (hasUnappliedChanges && gitSyncPending)
        {
            return "已保存未应用";
        }

        if (hasUnappliedChanges)
        {
            return "已保存，尚未应用";
        }

        if (gitSyncPending)
        {
            return "已保存，Git 未同步";
        }

        return "当前没有待处理变更";
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
