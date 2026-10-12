using ICP.Platform.Modeling.Metadata;
using System.Text.Json;
using ICP.Platform.Modules.MetadataConfigCenter.Models;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public class MetadataManifestService : IMetadataManifestService
{
    private const string SupportedSchemaVersion = "1.0";
    private static readonly HashSet<string> SupportedMergeModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "replace",
        "dic-entry"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _repositoryRoot;
    private readonly string? _runtimeRoot;
    private readonly ModuleMetadataStore _moduleMetadataStore;

    public MetadataManifestService()
        : this(ResolveRepositoryRoot(), ResolveRuntimeRoot())
    {
    }

    public MetadataManifestService(string repositoryRoot, string? runtimeRoot = null)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runtimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? null
            : Path.GetFullPath(runtimeRoot);
        _moduleMetadataStore = new ModuleMetadataStore(_repositoryRoot, _runtimeRoot);
    }

    // 合并当前可见 metadata；声明身份不会随前端目录 slug 改变。
    public async Task<IReadOnlyList<MetadataModuleManifest>> GetModuleManifestsAsync(CancellationToken cancellationToken = default)
    {
        var fileTypeMappings = await LoadFileTypeMappingsAsync(cancellationToken);
        var manifestsByModuleId = new Dictionary<string, MetadataModuleManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var modulesRoot in EnumerateModulesRoots())
        {
            if (!Directory.Exists(modulesRoot))
            {
                continue;
            }

            foreach (var moduleDirectory in Directory
                .EnumerateDirectories(modulesRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var moduleId = ModuleAuthoringPaths.ReadModuleId(moduleDirectory);
                if (manifestsByModuleId.ContainsKey(moduleId))
                {
                    continue;
                }

                var metadataDirectory = Path.Combine(moduleDirectory, "metadata");
                if (!Directory.Exists(metadataDirectory))
                {
                    continue;
                }

                var manifest = await BuildModuleManifestAsync(
                    moduleId,
                    metadataDirectory,
                    fileTypeMappings,
                    cancellationToken);

                if (manifest.Types.Count > 0)
                {
                    manifestsByModuleId[moduleId] = manifest;
                }
            }
        }

        return manifestsByModuleId.Values
            .OrderBy(manifest => manifest.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<MetadataModuleManifest> BuildModuleManifestAsync(
        string moduleId,
        string metadataDirectory,
        MetadataFileTypeMappingsDocument fileTypeMappings,
        CancellationToken cancellationToken)
    {
        var manifest = new MetadataModuleManifest
        {
            ModuleId = moduleId,
            MetadataRoot = "metadata",
            SchemaVersion = SupportedSchemaVersion
        };

        foreach (var filePath in Directory
            .EnumerateFiles(metadataDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativeFilePath = Path.GetRelativePath(metadataDirectory, filePath);
            var metadataFile = await ReadMetadataEnvelopeAsync(moduleId, filePath, cancellationToken);

            if (!string.Equals(metadataFile.SchemaVersion, SupportedSchemaVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateMetadataException(
                    moduleId,
                    filePath,
                    $"不支持的 schemaVersion '{metadataFile.SchemaVersion}'。");
            }

            if (string.IsNullOrWhiteSpace(metadataFile.Type))
            {
                throw CreateMetadataException(moduleId, filePath, "文件缺少 type。");
            }

            if (!fileTypeMappings.Mappings.TryGetValue(metadataFile.Type, out var fileTypeMapping))
            {
                throw CreateMetadataException(
                    moduleId,
                    filePath,
                    $"未知的 type '{metadataFile.Type}'。");
            }

            if (!SupportedMergeModes.Contains(fileTypeMapping.MergeMode))
            {
                throw CreateMetadataException(
                    moduleId,
                    filePath,
                    $"非法 mergeMode '{fileTypeMapping.MergeMode}'。");
            }

            if (string.Equals(metadataFile.Type, "dic", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fileTypeMapping.MergeMode, "dic-entry", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateMetadataException(
                    moduleId,
                    filePath,
                    "dic 类型必须使用 dic-entry mergeMode。");
            }

            if (string.Equals(fileTypeMapping.MergeMode, "dic-entry", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(metadataFile.Type, "dic", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateMetadataException(
                    moduleId,
                    filePath,
                    $"type '{metadataFile.Type}' 不能使用 dic-entry mergeMode。");
            }

            if (!manifest.Types.TryGetValue(metadataFile.Type, out var descriptor))
            {
                descriptor = new MetadataTypeDescriptor
                {
                    Enabled = true,
                    Type = metadataFile.Type,
                    SourcePath = GetSourcePath(metadataDirectory, filePath),
                    Adapter = fileTypeMapping.Adapter,
                    MergeMode = fileTypeMapping.MergeMode,
                    SchemaVersion = metadataFile.SchemaVersion
                };

                manifest.Types[metadataFile.Type] = descriptor;
            }

            if (!descriptor.SourceFiles.Any(item => string.Equals(item, relativeFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                descriptor.SourceFiles.Add(relativeFilePath);
            }
        }

        return manifest;
    }

    private static string GetSourcePath(string metadataDirectory, string filePath)
    {
        var relativeFilePath = Path.GetRelativePath(metadataDirectory, filePath);
        var sourcePath = Path.GetDirectoryName(relativeFilePath);
        return string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : sourcePath;
    }

    private static async Task<MetadataFileEnvelope> ReadMetadataEnvelopeAsync(
        string moduleId,
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var envelope = await JsonSerializer.DeserializeAsync<MetadataFileEnvelope>(stream, JsonOptions, cancellationToken);
            if (envelope is null)
            {
                throw CreateMetadataException(moduleId, filePath, "文件内容为空。");
            }

            return envelope;
        }
        catch (JsonException ex)
        {
            throw CreateMetadataException(moduleId, filePath, $"JSON 解析失败：{ex.Message}", ex);
        }
    }

    private async Task<MetadataFileTypeMappingsDocument> LoadFileTypeMappingsAsync(
        CancellationToken cancellationToken)
    {
        var fileTypeMappingsPathCandidates = GetPlatformMetadataFilePathCandidates("file-type-mappings.json").ToList();
        var fileTypeMappingsPath = fileTypeMappingsPathCandidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(fileTypeMappingsPath))
        {
            throw new InvalidOperationException(
                $"Platform file type mappings file was not found: {string.Join(", ", fileTypeMappingsPathCandidates)}");
        }

        try
        {
            await using var stream = File.OpenRead(fileTypeMappingsPath);
            var document = await JsonSerializer.DeserializeAsync<MetadataFileTypeMappingsDocument>(stream, JsonOptions, cancellationToken);
            if (document is null)
            {
                throw new InvalidOperationException($"Platform file type mappings file is empty: {fileTypeMappingsPath}");
            }

            if (!string.Equals(document.SchemaVersion, SupportedSchemaVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Platform file type mappings file '{fileTypeMappingsPath}' uses unsupported schemaVersion '{document.SchemaVersion}'.");
            }

            foreach (var (type, mapping) in document.Mappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Adapter))
                {
                    throw new InvalidOperationException(
                        $"Platform file type mapping '{fileTypeMappingsPath}' is missing adapter for type '{type}'.");
                }

                if (string.IsNullOrWhiteSpace(mapping.MergeMode) || !SupportedMergeModes.Contains(mapping.MergeMode))
                {
                    throw new InvalidOperationException(
                        $"Platform file type mapping '{fileTypeMappingsPath}' declares unsupported mergeMode '{mapping.MergeMode}' for type '{type}'.");
                }
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Platform file type mappings file '{fileTypeMappingsPath}' could not be parsed: {ex.Message}", ex);
        }
    }

    private static InvalidOperationException CreateMetadataException(string moduleId, string filePath, string message, Exception? innerException = null)
    {
        return new InvalidOperationException($"Module '{moduleId}' metadata file '{filePath}' failed validation: {message}", innerException);
    }

    // runtime 输出保持原路径，仓库读取合并后的 authoring 根。
    private IEnumerable<string> EnumerateModulesRoots()
    {
        if (!string.IsNullOrWhiteSpace(_runtimeRoot))
        {
            yield return Path.Combine(_runtimeRoot, "modules");
        }

        yield return Path.Combine(_repositoryRoot, "frontend", "modules");
    }

    private IEnumerable<string> GetPlatformMetadataFilePathCandidates(string fileName)
    {
        var primaryPath = _moduleMetadataStore.ResolvePlatformMetadataFilePath(fileName);
        yield return primaryPath;

        var repositoryRoot = ResolveRepositoryRoot();
        var fallbackPath = Path.Combine(repositoryRoot, "server", "platform", "metadata", fileName);
        if (!string.Equals(primaryPath, fallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return fallbackPath;
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for metadata manifest discovery.");
    }

    private static string ResolveRuntimeRoot()
    {
        var configuredInstallRoot = Environment.GetEnvironmentVariable("Platform__PluginInstallRoot");
        var installRoot = string.IsNullOrWhiteSpace(configuredInstallRoot)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : Path.IsPathRooted(configuredInstallRoot)
                ? configuredInstallRoot
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredInstallRoot));

        return Path.Combine(installRoot, "runtime");
    }

    private sealed class MetadataFileEnvelope
    {
        public string SchemaVersion { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class MetadataFileTypeMappingsDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;

        public Dictionary<string, MetadataFileTypeMapping> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MetadataFileTypeMapping
    {
        public string Adapter { get; set; } = string.Empty;

        public string MergeMode { get; set; } = "replace";
    }
}
