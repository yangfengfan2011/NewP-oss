using System.Text.Json;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public sealed class ConfigCenterModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath(
        "modules",
        "ConfigCenter",
        "metadata",
        "models");

    private static readonly string DefaultStorageTypeMapPath = GetRepositoryPath(
        "server",
        "platform-modeling",
        "metadata",
        "storage-types.json");

    private readonly string _modelDirectory;
    private readonly string _configItemPath;
    private readonly string _storageTypeMapPath;
    private readonly IModelMetadataDocumentLoader _modelLoader;
    private readonly IStorageTypeMapLoader _typeMapLoader;
    private readonly ModelMetadataValidator _validator;
    private readonly ModelMetadataProjector _projector;

    public ConfigCenterModelMetadataProvider()
        : this(Path.Combine(DefaultModelDirectory, "ConfigItem.model.json"), DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    internal ConfigCenterModelMetadataProvider(
        string configItemPath,
        string storageTypeMapPath,
        IModelMetadataDocumentLoader modelLoader)
    {
        _configItemPath = Path.GetFullPath(configItemPath);
        _modelDirectory = Path.GetDirectoryName(_configItemPath) ?? throw new InvalidOperationException("无法定位模型目录。");
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
        _modelLoader = modelLoader;
        _typeMapLoader = new JsonStorageTypeMapLoader();
        _validator = new ModelMetadataValidator();
        _projector = new ModelMetadataProjector();
    }

    /// <summary>
    /// 读取 ConfigCenter 的配置项模型定义。
    /// 旧入口保留不变，但读取逻辑统一收口到共享 loader。
    /// </summary>
    public ModelDefinition LoadConfigItem()
    {
        var typeMap = _typeMapLoader.LoadFromFile(_storageTypeMapPath);
        var document = LoadDocument(_configItemPath);

        _validator.Validate(document, typeMap);

        return _projector.Project(document, typeMap, "SqlServer");
    }

    /// <summary>
    /// 一次性返回 ConfigCenter 当前收口的两个模型定义。
    /// 外部只消费聚合后的 definitions，避免继续依赖单文件入口。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var typeMap = _typeMapLoader.LoadFromFile(_storageTypeMapPath);

        return new[]
        {
            LoadModel("ConfigItem.model.json", typeMap),
            LoadModel("ValueListItem.model.json", typeMap)
        };
    }

    private ModelDefinition LoadModel(string fileName, StorageTypeMapDocument typeMap)
    {
        var document = LoadDocument(Path.Combine(_modelDirectory, fileName));

        _validator.Validate(document, typeMap);

        return _projector.Project(document, typeMap, "SqlServer");
    }

    private ModelMetadataDocument LoadDocument(string path)
    {
        return _modelLoader.LoadFromFile(path);
    }

    // 模型输入随 authoring 根迁移，正式发布输出保持模块身份目录。
    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "server");
            if (Directory.Exists(candidate))
            {
                if (segments.Length >= 2 && segments[0] == "modules")
                {
                    return Path.GetFullPath(Path.Combine(
                        new[] { ModuleAuthoringPaths.ResolveModuleRoot(current.FullName, segments[1]) }
                            .Concat(segments.Skip(2)).ToArray()));
                }

                var pathSegments = new List<string> { current.FullName };
                pathSegments.AddRange(segments);
                return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
