using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public sealed class AuthorizationModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath(
        "modules",
        "Authorization",
        "metadata",
        "models");

    private static readonly string DefaultStorageTypeMapPath = GetRepositoryPath(
        "server",
        "platform-modeling",
        "metadata",
        "storage-types.json");

    private readonly string _modelDirectory;
    private readonly string _storageTypeMapPath;
    private readonly IModelMetadataDocumentLoader _modelLoader;
    private readonly IStorageTypeMapLoader _storageTypeMapLoader;
    private readonly ModelMetadataValidator _metadataValidator;
    private readonly ModelMetadataProjector _projector;

    public AuthorizationModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    internal AuthorizationModelMetadataProvider(
        string modelDirectory,
        string storageTypeMapPath,
        IModelMetadataDocumentLoader modelLoader)
    {
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
        _modelLoader = modelLoader;
        _storageTypeMapLoader = new JsonStorageTypeMapLoader();
        _metadataValidator = new ModelMetadataValidator();
        _projector = new ModelMetadataProjector();
    }

    /// <summary>
    /// 一次性读取授权模块模型真源并返回运行时定义。
    /// 外部只消费聚合后的 definitions，避免继续依赖单模型入口形成新的分叉契约。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storageTypeMap = _storageTypeMapLoader.LoadFromFile(_storageTypeMapPath);

        return new[]
        {
            LoadModel("Role.model.json", storageTypeMap),
            LoadModel("DataScopeRuleNode.model.json", storageTypeMap),
            LoadModel("DataScopeRuleSubject.model.json", storageTypeMap),
            LoadModel("DataScopeObjectSubject.model.json", storageTypeMap),
            LoadModel("DataScopeObjectAction.model.json", storageTypeMap),
            LoadModel("FunctionPoint.model.json", storageTypeMap),
            LoadModel("RoleFunctionGrant.model.json", storageTypeMap),
            LoadModel("Permission.model.json", storageTypeMap),
            LoadModel("RolePermission.model.json", storageTypeMap),
            LoadModel("UserRole.model.json", storageTypeMap)
        };
    }

    // 真源读取、校验和投影都收口在 provider，外部只拿到最终 definition。
    private ModelDefinition LoadModel(string fileName, StorageTypeMapDocument storageTypeMap)
    {
        var modelPath = Path.Combine(_modelDirectory, fileName);
        var document = _modelLoader.LoadFromFile(modelPath);

        _metadataValidator.Validate(document, storageTypeMap);

        return _projector.Project(document, storageTypeMap, "SqlServer");
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
