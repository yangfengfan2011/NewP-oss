using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Example.Persistence.Descriptors;

public sealed class ExampleModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath(
        "modules",
        "Example",
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

    // 生产与 design-time 共用同一个 metadata provider，避免迁移模型形成平行真源。
    public ExampleModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    // 可替换路径只服务 owner 测试，投影和校验仍使用正式 modeling 组件。
    internal ExampleModelMetadataProvider(
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
    /// 从 Example 的可提交 authoring 真源读取并投影当前模型定义。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storageTypeMap = _storageTypeMapLoader.LoadFromFile(_storageTypeMapPath);
        var modelPath = Path.Combine(_modelDirectory, "ExampleRecord.model.json");
        var document = _modelLoader.LoadFromFile(modelPath);
        _validator.Validate(document, storageTypeMap);

        return [_projector.Project(document, storageTypeMap, "SqlServer")];
    }

    // 从构建输出向上定位仓库根，使 design-time 与测试均读取提交态 authoring 文件。
    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
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
