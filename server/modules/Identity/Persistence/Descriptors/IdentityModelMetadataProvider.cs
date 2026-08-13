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

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return Path.GetFullPath(Path.Combine([current.FullName, .. segments]));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
