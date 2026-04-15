using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Models;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

/// <summary>
/// 将 SettingDefinition 的 authoring 真源投影到运行时目录。
/// Save 只更新可提交源，Apply 才负责切换 runtime 视图，两者故意分离。
/// </summary>
public class SettingDefinitionApplyService : ISettingDefinitionApplyService
{
    private readonly IMetadataManifestService _metadataManifestService;
    private readonly ModuleMetadataStore _moduleMetadataStore;
    private readonly Action<string, string> _moveDirectory;

    public SettingDefinitionApplyService(
        IMetadataManifestService metadataManifestService,
        ModuleMetadataStore moduleMetadataStore)
        : this(metadataManifestService, moduleMetadataStore, Directory.Move)
    {
    }

    internal SettingDefinitionApplyService(
        IMetadataManifestService metadataManifestService,
        ModuleMetadataStore moduleMetadataStore,
        Action<string, string> moveDirectory)
    {
        _metadataManifestService = metadataManifestService;
        _moduleMetadataStore = moduleMetadataStore;
        _moveDirectory = moveDirectory;
    }

    public async Task<ApplySettingDefinitionsResult> ApplySettingDefinitionsAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var settingsSourcePath = await GetSettingsSourcePathAsync(moduleId, cancellationToken);
        var sourceDirectory = _moduleMetadataStore.ResolveAuthoringMetadataDirectory(moduleId, settingsSourcePath);
        var runtimeDirectory = _moduleMetadataStore.ResolveRuntimeMetadataDirectory(moduleId, settingsSourcePath);

        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new InvalidOperationException($"Module {moduleId} does not have an available runtime metadata projection root.");
        }

        // 只有 authoring 与 runtime 投影不一致时才执行替换，
        // 这样前端的“当前没有待处理变更”状态才能和实际生效结果保持一致。
        var hadPendingApply = !MetadataDirectoriesMatch(sourceDirectory, runtimeDirectory);
        if (hadPendingApply)
        {
            await ReplaceRuntimeProjectionAsync(sourceDirectory, runtimeDirectory, cancellationToken);
        }

        return new ApplySettingDefinitionsResult
        {
            Status = hadPendingApply ? "已应用" : "当前没有待处理变更",
            HasUnappliedChanges = false
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

    private static bool MetadataDirectoriesMatch(string sourceDirectory, string runtimeDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return !Directory.Exists(runtimeDirectory);
        }

        if (!Directory.Exists(runtimeDirectory))
        {
            return false;
        }

        var sourceFiles = Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var runtimeFiles = Directory
            .EnumerateFiles(runtimeDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!sourceFiles.SequenceEqual(runtimeFiles, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // 比较前先统一换行，避免 Windows/Linux 行尾差异导致无意义的“未应用”提示。
        foreach (var fileName in sourceFiles)
        {
            var sourceContent = NormalizeLineEndings(File.ReadAllText(Path.Combine(sourceDirectory, fileName!)));
            var runtimeContent = NormalizeLineEndings(File.ReadAllText(Path.Combine(runtimeDirectory, fileName!)));
            if (!ContentsMatch(sourceContent, runtimeContent))
            {
                return false;
            }
        }

        return true;
    }

    private async Task ReplaceRuntimeProjectionAsync(string sourceDirectory, string runtimeDirectory, CancellationToken cancellationToken)
    {
        var runtimeParentDirectory = Path.GetDirectoryName(runtimeDirectory)
            ?? throw new InvalidOperationException($"Unable to resolve runtime parent directory for '{runtimeDirectory}'.");
        Directory.CreateDirectory(runtimeParentDirectory);

        var stagingDirectory = Path.Combine(runtimeParentDirectory, $".apply-staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(runtimeParentDirectory, $".apply-backup-{Guid.NewGuid():N}");
        var runtimeExists = Directory.Exists(runtimeDirectory);
        var runtimeReplaced = false;

        try
        {
            // 先构建 staging，再整体替换 runtime 目录；
            // 如果提交阶段失败，catch 会把 backup 挪回去，避免 runtime 落在半更新状态。
            await ProjectDirectoryAsync(sourceDirectory, stagingDirectory, cancellationToken);

            if (runtimeExists)
            {
                _moveDirectory(runtimeDirectory, backupDirectory);
            }

            _moveDirectory(stagingDirectory, runtimeDirectory);
            runtimeReplaced = true;

            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch
        {
            if (runtimeReplaced && Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }

            // 只有原 runtime 存在时才做恢复，避免把一个本不存在的目录错误“回滚”出来。
            if (runtimeExists && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, runtimeDirectory);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private static async Task ProjectDirectoryAsync(string sourceDirectory, string runtimeDirectory, CancellationToken cancellationToken)
    {
        if (Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }

        Directory.CreateDirectory(runtimeDirectory);
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var targetPath = Path.Combine(runtimeDirectory, Path.GetFileName(file));
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
        }
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static bool ContentsMatch(string sourceContent, string runtimeContent)
    {
        if (TryParseJson(sourceContent, out var sourceJson) && TryParseJson(runtimeContent, out var runtimeJson))
        {
            // Apply 阶段只关心 runtime 是否与 authoring 语义一致，不应把 JSON 属性顺序视为未应用变更。
            return JsonNode.DeepEquals(sourceJson, runtimeJson);
        }

        return sourceContent.Equals(runtimeContent, StringComparison.Ordinal);
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
