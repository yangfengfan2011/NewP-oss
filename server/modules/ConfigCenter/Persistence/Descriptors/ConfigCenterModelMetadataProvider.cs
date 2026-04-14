using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public sealed class ConfigCenterModelMetadataProvider
{
    private static readonly string ConfigItemPath = GetRepositoryPath(
        "server",
        "modules",
        "ConfigCenter",
        "metadata",
        "models",
        "ConfigItem.model.json");

    private static readonly string StorageTypeMapPath = GetRepositoryPath(
        "server",
        "platform-modeling",
        "metadata",
        "storage-types.json");

    private readonly IModelMetadataDocumentLoader _modelLoader = new JsonModelMetadataDocumentLoader();
    private readonly IStorageTypeMapLoader _typeMapLoader = new JsonStorageTypeMapLoader();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    public ModelDefinition LoadConfigItem()
    {
        // 文件读取、校验和投影收口在 provider 中，避免散落到静态定义容器。
        var document = _modelLoader.LoadFromFile(ConfigItemPath);
        var typeMap = _typeMapLoader.LoadFromFile(StorageTypeMapPath);

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
