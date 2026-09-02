using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Organization.Persistence.Descriptors;

public sealed class OrganizationModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath(
        "modules",
        "Organization",
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
    private readonly ModelMetadataValidator _validator;
    private readonly ModelMetadataProjector _projector;

    public OrganizationModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    internal OrganizationModelMetadataProvider(
        string modelDirectory,
        string storageTypeMapPath,
        IModelMetadataDocumentLoader modelLoader)
    {
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
        _modelLoader = modelLoader;
        _storageTypeMapLoader = new JsonStorageTypeMapLoader();
        _validator = new ModelMetadataValidator();
        _projector = new ModelMetadataProjector();
    }

    /// <summary>
    /// 一次性读取 Organization 的四个模型真源并返回运行时定义。
    /// 外部只消费聚合后的 definitions，避免继续依赖单模型入口形成新的分叉契约。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storageTypeMap = _storageTypeMapLoader.LoadFromFile(_storageTypeMapPath);

        return new[]
        {
            LoadModel("Company.model.json", storageTypeMap),
            LoadModel("Department.model.json", storageTypeMap),
            LoadModel("Position.model.json", storageTypeMap),
            LoadModel("Person.model.json", storageTypeMap)
        };
    }

    /// <summary>
    /// 读取单个 Organization Model authoring 文档，供同模块 descriptor 投影共享字段事实。
    /// </summary>
    public ModelMetadataDocument GetDocument(string modelName)
    {
        var fileName = modelName switch
        {
            "Company" => "Company.model.json",
            "Department" => "Department.model.json",
            "Position" => "Position.model.json",
            "Person" => "Person.model.json",
            _ => throw new ArgumentException($"未知 Organization Model：{modelName}", nameof(modelName))
        };

        return _modelLoader.LoadFromFile(Path.Combine(_modelDirectory, fileName));
    }

    // 真源加载、校验和投影都收口在 provider，外部只拿到最终 definition。
    private ModelDefinition LoadModel(string fileName, StorageTypeMapDocument storageTypeMap)
    {
        var modelPath = Path.Combine(_modelDirectory, fileName);
        var document = _modelLoader.LoadFromFile(modelPath);

        _validator.Validate(document, storageTypeMap);

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
