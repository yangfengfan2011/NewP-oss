using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Files;

/// <summary>运行时、设计时和迁移只消费本模块正式 authoring。</summary>
public sealed class FilesModelMetadataProvider : IModelDefinitionProvider
{
    /// <summary>一次装载并投影 Files 自有 Entity，不借用业务模型或数据库。</summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var root = MetadataRoot();
        var storage = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(root, "server/platform-modeling/metadata/storage-types.json"));
        var moduleRoot = ModuleAuthoringPaths.ResolveModuleRoot(root, "Files");
        return new[] { "FileRecord", "FileUploadLease", "FileUsage", "FileCopyLease" }.Select(name =>
        {
            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(moduleRoot, "metadata/models", name + ".model.json"));
            new ModelMetadataValidator().Validate(document, storage);
            return new ModelMetadataProjector().Project(document, storage, "SqlServer") with { OwnerModuleId = "Files" };
        }).ToArray();
    }

    // 正式输出包含同一真源；设计期只向上查找仓库，不另建模型副本。
    private static string MetadataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Files"), "metadata/models/FileRecord.model.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位 Files 元数据。");
    }
}
