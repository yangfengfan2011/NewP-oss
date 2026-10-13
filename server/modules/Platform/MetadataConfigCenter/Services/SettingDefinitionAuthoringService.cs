using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Models;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

/// <summary>
/// 面向 authoring 真源的 SettingDefinition 读写服务。
/// 这里负责维护可提交源目录的内容与状态描述，不直接修改 runtime 生效视图。
/// </summary>
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
        var sourceFiles = await GetSettingsSourceFilesAsync(moduleId, cancellationToken);

        var definitions = new List<SettingDefinition>();
        // 每个文件先带上版本摘要，再统一校验；
        // 这样前端既能知道定义来自哪个源文件，也能识别内容是否发生过 authoring 级变更。
        foreach (var relativeFilePath in sourceFiles)
        {
            var filePath = _moduleMetadataStore.ResolveMetadataRelativeFilePath(moduleId, relativeFilePath);
            var content = await _moduleMetadataStore.ReadTextAsync(filePath, cancellationToken);
            var versionSummary = _moduleMetadataStore.ComputeVersionSummary(content);
            var parsedDefinitions = _adapter.ParseDefinitions(relativeFilePath, content);
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
        var descriptor = await GetSettingsDescriptorAsync(moduleId, cancellationToken);
        var definitions = request.Definitions.Select(MapToModel).ToList();

        _adapter.Validate(definitions);

        var generatedFiles = _adapter.BuildFileContents(moduleId, definitions);
        ValidateGeneratedFiles(descriptor, generatedFiles);
        var gitSyncPending = false;
        var hasUnappliedChanges = false;

        foreach (var generatedFile in generatedFiles)
        {
            var filePath = _moduleMetadataStore.ResolveAuthoringMetadataRelativeFilePath(moduleId, generatedFile.SourceFile);
            if (!await FileContentMatchesAsync(filePath, generatedFile.Content, cancellationToken))
            {
                gitSyncPending = true;
            }

            var runtimeFilePath = _moduleMetadataStore.ResolveRuntimeMetadataRelativeFilePath(moduleId, generatedFile.SourceFile);
            // runtime 不存在或内容不一致都视为“已保存未应用”，
            // 因为 authoring 成功并不等于当前运行时已经切到新版本。
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

    private async Task<MetadataTypeDescriptor> GetSettingsDescriptorAsync(string moduleId, CancellationToken cancellationToken)
    {
        var manifests = await _metadataManifestService.GetModuleManifestsAsync(cancellationToken);
        var manifest = manifests.SingleOrDefault(item => string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
        if (manifest is null || !manifest.Types.TryGetValue("settings", out var settingsDescriptor) || !settingsDescriptor.Enabled)
        {
            throw new InvalidOperationException($"Module {moduleId} does not expose editable settings metadata.");
        }

        return settingsDescriptor;
    }

    private async Task<IReadOnlyList<string>> GetSettingsSourceFilesAsync(string moduleId, CancellationToken cancellationToken)
    {
        var descriptor = await GetSettingsDescriptorAsync(moduleId, cancellationToken);
        if (descriptor.SourceFiles.Count > 0)
        {
            return descriptor.SourceFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var files = _moduleMetadataStore.EnumerateMetadataFiles(moduleId, descriptor.SourcePath, "*.json");
        return files
            .Select(file => string.IsNullOrWhiteSpace(descriptor.SourcePath)
                ? Path.GetFileName(file)
                : Path.Combine(descriptor.SourcePath, Path.GetFileName(file)))
            .ToArray();
    }

    private static void ValidateGeneratedFiles(
        MetadataTypeDescriptor descriptor,
        IReadOnlyList<SettingDefinitionAuthoringAdapter.GeneratedSettingDefinitionFile> generatedFiles)
    {
        if (descriptor.SourceFiles.Count == 0)
        {
            return;
        }

        var allowedFiles = descriptor.SourceFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpectedFile = generatedFiles
            .Select(file => file.SourceFile)
            .FirstOrDefault(sourceFile => !allowedFiles.Contains(sourceFile));
        if (!string.IsNullOrWhiteSpace(unexpectedFile))
        {
            throw new InvalidOperationException($"Settings metadata file '{unexpectedFile}' is outside the manifest-declared SourceFiles set.");
        }
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
        return ContentsMatch(actualContent, expectedContent);
    }

    private static string BuildStatus(bool gitSyncPending, bool hasUnappliedChanges)
    {
        // 状态文案按 authoring / apply / git 三类信号组合，
        // 让前端不需要重复推导复杂状态机。
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

    private static bool ContentsMatch(string actualContent, string expectedContent)
    {
        if (TryParseJson(actualContent, out var actualJson) && TryParseJson(expectedContent, out var expectedJson))
        {
            // settings 进入对象映射后，序列化属性顺序不应再影响“是否有真实待应用变更”的判定。
            return JsonNode.DeepEquals(actualJson, expectedJson);
        }

        return NormalizeLineEndings(actualContent).Equals(NormalizeLineEndings(expectedContent), StringComparison.Ordinal);
    }

    private static bool TryParseJson(string content, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(content);
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }
}
