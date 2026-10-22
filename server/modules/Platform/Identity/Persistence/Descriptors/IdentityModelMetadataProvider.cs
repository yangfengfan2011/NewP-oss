using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Identity.Persistence.Descriptors;

public sealed class IdentityModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath("modules", "Identity", "metadata", "models");
    private static readonly string DefaultStorageTypeMapPath = GetRepositoryPath("server", "platform-modeling", "metadata", "storage-types.json");

    private readonly string _modelDirectory;
    private readonly string _storageTypeMapPath;

    public IdentityModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath)
    {
    }

    internal IdentityModelMetadataProvider(string modelDirectory, string storageTypeMapPath)
    {
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
    }

    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var loader = new JsonModelMetadataDocumentLoader();
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(_storageTypeMapPath);
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();

        return new[] { "IdentityAccount.model.json", "LocalCredential.model.json", "IdentitySecurityEvent.model.json" }
            .Select(fileName =>
            {
                var document = loader.LoadFromFile(Path.Combine(_modelDirectory, fileName));
                validator.Validate(document, typeMap);
                return projector.Project(document, typeMap, "SqlServer") with { OwnerModuleId = "Identity" };
            })
            .ToArray();
    }

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
                    return Path.GetFullPath(Path.Combine([
                        ModuleAuthoringPaths.ResolveModuleRoot(current.FullName, segments[1]), .. segments.Skip(2)]));
                }

                return Path.GetFullPath(Path.Combine([current.FullName, .. segments]));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
