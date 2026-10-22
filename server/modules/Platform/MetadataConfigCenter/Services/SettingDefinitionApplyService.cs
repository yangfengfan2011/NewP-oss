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
        var sourceFiles = await GetSettingsSourceFilesAsync(moduleId, cancellationToken);
        var runtimeMetadataRoot = _moduleMetadataStore.ResolveRuntimeMetadataDirectory(moduleId, string.Empty);

        if (string.IsNullOrWhiteSpace(runtimeMetadataRoot))
        {
            throw new InvalidOperationException($"Module {moduleId} does not have an available runtime metadata projection root.");
        }

        // 只有 authoring 与 runtime 投影不一致时才执行替换，
        // 这样前端的“当前没有待处理变更”状态才能和实际生效结果保持一致。
        var hadPendingApply = await HasPendingApplyAsync(moduleId, sourceFiles, cancellationToken);
        if (hadPendingApply)
        {
            await ReplaceRuntimeProjectionAsync(moduleId, sourceFiles, runtimeMetadataRoot, cancellationToken);
        }

        return new ApplySettingDefinitionsResult
        {
            Status = hadPendingApply ? "已应用" : "当前没有待处理变更",
            HasUnappliedChanges = false
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

    private async Task<bool> HasPendingApplyAsync(
        string moduleId,
        IReadOnlyList<string> sourceFiles,
        CancellationToken cancellationToken)
    {
        var runtimeSettingsFiles = await EnumerateRuntimeSettingsFilesAsync(moduleId, cancellationToken);
        if (!sourceFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(runtimeSettingsFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // 比较前先统一换行，避免 Windows/Linux 行尾差异导致无意义的“未应用”提示。
        foreach (var fileName in sourceFiles)
        {
            var sourcePath = _moduleMetadataStore.ResolveAuthoringMetadataRelativeFilePath(moduleId, fileName);
            var runtimePath = _moduleMetadataStore.ResolveRuntimeMetadataRelativeFilePath(moduleId, fileName);
            if (string.IsNullOrWhiteSpace(runtimePath) || !File.Exists(runtimePath))
            {
                return true;
            }

            var sourceContent = NormalizeLineEndings(await File.ReadAllTextAsync(sourcePath, cancellationToken));
            var runtimeContent = NormalizeLineEndings(await File.ReadAllTextAsync(runtimePath, cancellationToken));
            if (!ContentsMatch(sourceContent, runtimeContent))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ReplaceRuntimeProjectionAsync(
        string moduleId,
        IReadOnlyList<string> sourceFiles,
        string runtimeMetadataRoot,
        CancellationToken cancellationToken)
    {
        var runtimeParentDirectory = Path.GetDirectoryName(runtimeMetadataRoot)
            ?? throw new InvalidOperationException($"Unable to resolve runtime parent directory for '{runtimeMetadataRoot}'.");
        Directory.CreateDirectory(runtimeParentDirectory);

        var stagingDirectory = Path.Combine(runtimeParentDirectory, $".apply-staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(runtimeParentDirectory, $".apply-backup-{Guid.NewGuid():N}");
        var runtimeExists = Directory.Exists(runtimeMetadataRoot);
        var runtimeReplaced = false;

        try
        {
            await PrepareStagingMetadataRootAsync(moduleId, sourceFiles, runtimeMetadataRoot, stagingDirectory, cancellationToken);

            if (runtimeExists)
            {
                _moveDirectory(runtimeMetadataRoot, backupDirectory);
            }

            _moveDirectory(stagingDirectory, runtimeMetadataRoot);
            runtimeReplaced = true;

            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch
        {
            if (runtimeReplaced && Directory.Exists(runtimeMetadataRoot))
            {
                Directory.Delete(runtimeMetadataRoot, recursive: true);
            }

            // 只有原 runtime 存在时才做恢复，避免把一个本不存在的目录错误“回滚”出来。
            if (runtimeExists && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, runtimeMetadataRoot);
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

    private async Task PrepareStagingMetadataRootAsync(
        string moduleId,
        IReadOnlyList<string> sourceFiles,
        string runtimeMetadataRoot,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        if (Directory.Exists(runtimeMetadataRoot))
        {
            CopyDirectory(runtimeMetadataRoot, stagingDirectory);
        }
        else
        {
            Directory.CreateDirectory(stagingDirectory);
        }

        foreach (var relativeFilePath in sourceFiles)
        {
            var sourcePath = _moduleMetadataStore.ResolveAuthoringMetadataRelativeFilePath(moduleId, relativeFilePath);
            var targetPath = Path.Combine(stagingDirectory, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
        }

        var stagedSettingsFiles = await EnumerateSettingsFilesAsync(stagingDirectory, cancellationToken);
        foreach (var runtimeSettingsFile in stagedSettingsFiles)
        {
            if (sourceFiles.Contains(runtimeSettingsFile, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(Path.Combine(stagingDirectory, runtimeSettingsFile));
        }
    }

    private async Task<IReadOnlyList<string>> EnumerateRuntimeSettingsFilesAsync(string moduleId, CancellationToken cancellationToken)
    {
        var runtimeMetadataRoot = _moduleMetadataStore.ResolveRuntimeMetadataDirectory(moduleId, string.Empty);
        if (string.IsNullOrWhiteSpace(runtimeMetadataRoot))
        {
            return [];
        }

        return await EnumerateSettingsFilesAsync(runtimeMetadataRoot, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> EnumerateSettingsFilesAsync(string metadataRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(metadataRoot))
        {
            return [];
        }

        var settingsFiles = new List<string>();
        foreach (var filePath in Directory.EnumerateFiles(metadataRoot, "*.json", SearchOption.AllDirectories))
        {
            if (!await IsSettingsMetadataFileAsync(filePath, cancellationToken))
            {
                continue;
            }

            settingsFiles.Add(Path.GetRelativePath(metadataRoot, filePath));
        }

        return settingsFiles
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<bool> IsSettingsMetadataFileAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "settings", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
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
