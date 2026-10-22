using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Audit.Persistence.Descriptors;

public sealed class AuditModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath(
        "modules",
        "Audit",
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
    public AuditModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath, new JsonModelMetadataDocumentLoader())
    {
    }

    // 可替换路径只服务 owner 测试，投影和校验仍使用正式 modeling 组件。
    internal AuditModelMetadataProvider(
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
    /// 从 Audit 的可提交 authoring 真源读取并投影当前模型定义。
    /// </summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storageTypeMap = _storageTypeMapLoader.LoadFromFile(_storageTypeMapPath);
        var document = GetDocument(storageTypeMap);

        return [_projector.Project(document, storageTypeMap, "SqlServer")];
    }

    // CRUD 与 persistence 必须消费同一份已校验 authoring 文档，避免由模块工厂重新声明字段语义。
    internal ModelMetadataDocument GetDocument()
    {
        var storageTypeMap = _storageTypeMapLoader.LoadFromFile(_storageTypeMapPath);
        return GetDocument(storageTypeMap);
    }

    // 单次加载同时完成 owner metadata 校验，调用方不能取得未经验证的字段事实。
    private ModelMetadataDocument GetDocument(StorageTypeMapDocument storageTypeMap)
    {
        var modelPath = Path.Combine(_modelDirectory, "BusinessChange.model.json");
        var document = _modelLoader.LoadFromFile(modelPath);
        _validator.Validate(document, storageTypeMap);
        return document;
    }

    // 从构建输出向上定位仓库根，使 design-time 与测试均读取提交态 authoring 文件。
    // 模型输入随 authoring 根迁移，正式发布输出保持模块身份目录。
    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
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
