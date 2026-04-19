using System.Text.Json;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public sealed class ConfigCenterModelMetadataProvider
{
    private static readonly string DefaultConfigItemPath = GetRepositoryPath(
        "modules",
        "ConfigCenter",
        "metadata",
        "models",
        "ConfigItem.model.json");

    private static readonly string DefaultStorageTypeMapPath = GetRepositoryPath(
        "server",
        "platform-modeling",
        "metadata",
        "storage-types.json");

    private readonly string _configItemPath;
    private readonly string _storageTypeMapPath;
    private readonly IModelMetadataDocumentLoader _modelLoader;
    private readonly IStorageTypeMapLoader _typeMapLoader;
    private readonly ModelMetadataValidator _validator;
    private readonly ModelMetadataProjector _projector;

    public ConfigCenterModelMetadataProvider()
        : this(DefaultConfigItemPath, DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    internal ConfigCenterModelMetadataProvider(
        string configItemPath,
        string storageTypeMapPath,
        IModelMetadataDocumentLoader modelLoader)
    {
        _configItemPath = Path.GetFullPath(configItemPath);
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
        _modelLoader = modelLoader;
        _typeMapLoader = new JsonStorageTypeMapLoader();
        _validator = new ModelMetadataValidator();
        _projector = new ModelMetadataProjector();
    }

    /// <summary>
    /// 读取 ConfigCenter 的配置项模型定义。
    /// 模型 authoring 语义统一交给共享 loader 解析，provider 只负责装配验证与投影。
    /// </summary>
    public ModelDefinition LoadConfigItem()
    {
        var document = _modelLoader.LoadFromFile(_configItemPath);
        var typeMap = _typeMapLoader.LoadFromFile(_storageTypeMapPath);

        _validator.Validate(document, typeMap);

        return _projector.Project(document, typeMap, "SqlServer");
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "server");
            if (Directory.Exists(candidate))
            {
                var pathSegments = new List<string> { current.FullName };
                pathSegments.AddRange(segments);
                return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
