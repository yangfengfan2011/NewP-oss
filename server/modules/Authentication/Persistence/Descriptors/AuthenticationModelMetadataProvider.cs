using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Authentication.Persistence.Descriptors;

public sealed class AuthenticationModelMetadataProvider : IModelDefinitionProvider
{
    private static readonly string DefaultModelDirectory = GetRepositoryPath("modules", "Authentication", "metadata", "models");
    private static readonly string DefaultStorageTypeMapPath = GetRepositoryPath("server", "platform-modeling", "metadata", "storage-types.json");

    private readonly string _modelDirectory;
    private readonly string _storageTypeMapPath;

    public AuthenticationModelMetadataProvider()
        : this(DefaultModelDirectory, DefaultStorageTypeMapPath)
    {
    }

    internal AuthenticationModelMetadataProvider(string modelDirectory, string storageTypeMapPath)
    {
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _storageTypeMapPath = Path.GetFullPath(storageTypeMapPath);
    }

    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var loader = new JsonModelMetadataDocumentLoader();
        var document = loader.LoadFromFile(Path.Combine(_modelDirectory, "AuthenticationSession.model.json"));
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(_storageTypeMapPath);
        new ModelMetadataValidator().Validate(document, typeMap);

        return
        [
            new ModelMetadataProjector().Project(document, typeMap, "SqlServer") with
            {
                OwnerModuleId = "Authentication"
            }
        ];
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
