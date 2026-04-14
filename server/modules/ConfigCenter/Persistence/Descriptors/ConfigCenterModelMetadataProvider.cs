using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

/// <summary>
/// 负责从模块内模型元数据文件生成配置中心模型定义。
/// </summary>
public sealed class ConfigCenterModelMetadataProvider
{
    private const string DefaultProvider = "SqlServer";

    // 试点阶段先在进程内缓存解析结果，保证 Definitions 与测试看到的是同一批模型定义。
    private static readonly Lazy<IReadOnlyDictionary<string, ModelDefinition>> CachedDefinitions =
        new(LoadDefinitions, isThreadSafe: true);

    /// <summary>
    /// 返回配置中心模块的全部模型定义。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetAll()
    {
        return CachedDefinitions.Value.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 按名称返回必须存在的模型定义。
    /// </summary>
    public ModelDefinition GetRequired(string name)
    {
        if (CachedDefinitions.Value.TryGetValue(name, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"配置中心模型元数据中未找到模型 '{name}'。");
    }

    private static IReadOnlyDictionary<string, ModelDefinition> LoadDefinitions()
    {
        var modelDirectory = ResolveModelMetadataDirectory();
        var typeMapPath = ResolveStorageTypeMapPath();
        var documentLoader = new JsonModelMetadataDocumentLoader();
        var typeMapLoader = new JsonStorageTypeMapLoader();
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();
        var typeMap = typeMapLoader.Load(typeMapPath);

        var definitions = Directory.GetFiles(modelDirectory, "*.model.json")
            .Select(documentLoader.Load)
            .Select(document =>
            {
                validator.Validate(document, typeMap);
                return projector.Project(document, typeMap, DefaultProvider);
            })
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException($"配置中心模型元数据目录 '{modelDirectory}' 中没有可加载的模型文件。");
        }

        return definitions;
    }

    private static string ResolveModelMetadataDirectory()
    {
        foreach (var candidate in BuildCandidateDirectories())
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("找不到 ConfigCenter 模型元数据目录。");
    }

    private static string ResolveStorageTypeMapPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json");

        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException("找不到平台存储类型映射文件。", path);
    }

    private static IEnumerable<string> BuildCandidateDirectories()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "metadata", "models");
            current = current.Parent;
        }

        yield return Path.Combine(FindRepositoryRoot(), "server", "modules", "ConfigCenter", "metadata", "models");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}
