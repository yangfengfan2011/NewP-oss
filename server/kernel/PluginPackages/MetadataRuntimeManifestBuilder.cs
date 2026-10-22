using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Kernel.PluginPackages;

/// <summary>
/// 基于已投影的 runtime/modules 目录生成前端可发现的 metadata runtime 清单。
/// 这里只负责暴露 nav/view/model 的文件索引，不在内核侧引入第二套 authoring 或页面解释逻辑。
/// </summary>
public sealed class MetadataRuntimeManifestBuilder
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 按当前生效顺序输出具有前端投影的模块 metadata runtime 清单。
    /// </summary>
    /// <param name="runtimeRoot">staging 后的 runtime 根目录。</param>
    /// <param name="moduleOrder">当前生效模块顺序。</param>
    /// <param name="frontendModuleIds">Canonical Module Plan 中具有前端入口的模块。</param>
    /// <returns>只包含可由前端发现的模块 metadata 索引的 manifest JSON。</returns>
    public string Build(
        string runtimeRoot,
        IReadOnlyList<string> moduleOrder,
        IReadOnlyCollection<string> frontendModuleIds,
        IReadOnlyList<FeaturePoint>? features = null,
        IReadOnlyList<ActionDataAccessRequirement>? dataAccess = null)
    {
        var frontendModules = frontendModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modules = moduleOrder
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(frontendModules.Contains)
            .Select(moduleId => new MetadataRuntimeModuleEntry(
                moduleId,
                $"/plugins/runtime/modules/{moduleId}/metadata",
                EnumerateMetadataFiles(runtimeRoot, moduleId, "navigation"),
                EnumerateMetadataFiles(runtimeRoot, moduleId, "views"),
                EnumerateMetadataFiles(runtimeRoot, moduleId, "models"),
                (features ?? []).Where(point => point.ModuleId == moduleId).ToArray(),
                (dataAccess ?? []).Where(requirement => requirement.ModuleId == moduleId).ToArray()))
            .Where(module => module.NavFiles.Count > 0 || module.ViewFiles.Count > 0 || module.ModelFiles.Count > 0)
            .ToArray();

        return JsonSerializer.Serialize(new MetadataRuntimeManifest(modules), ManifestJsonOptions) + Environment.NewLine;
    }

    private static IReadOnlyList<string> EnumerateMetadataFiles(string runtimeRoot, string moduleId, string relativeDirectory)
    {
        var directoryPath = Path.Combine(runtimeRoot, "modules", moduleId, "metadata", relativeDirectory);
        if (!Directory.Exists(directoryPath))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directoryPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(Path.Combine(runtimeRoot, "modules", moduleId, "metadata"), path)
                .Replace('\\', '/'))
            .ToArray();
    }

    private sealed record MetadataRuntimeManifest(IReadOnlyList<MetadataRuntimeModuleEntry> Modules);

    private sealed record MetadataRuntimeModuleEntry(
        string ModuleId,
        string MetadataBasePath,
        IReadOnlyList<string> NavFiles,
        IReadOnlyList<string> ViewFiles,
        IReadOnlyList<string> ModelFiles,
        IReadOnlyList<FeaturePoint> Features,
        IReadOnlyList<ActionDataAccessRequirement> DataAccess);
}
