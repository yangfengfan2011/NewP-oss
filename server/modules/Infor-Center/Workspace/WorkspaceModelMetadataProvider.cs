using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Workspace;

/// <summary>迁移与定向测试复用正式 authoring 投影，不维护第二份字段定义。</summary>
public sealed class WorkspaceModelMetadataProvider : IModelDefinitionProvider
{
    /// <summary>从仓库 authoring 装载工作空间实体与内容关系。</summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Workspace"), "metadata", "models", "WorkspaceFolder.model.json")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("无法定位 Workspace authoring。");
        var storage = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(root, "server/platform-modeling/metadata/storage-types.json"));
        return new[] { "WorkspaceFolder", "WorkspaceSpace", "WorkspaceFolderContent", "WorkspaceObjectCopy" }.Select(name =>
        {
            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(root, "Workspace"), "metadata/models", name + ".model.json"));
            new ModelMetadataValidator().Validate(document, storage);
            return new ModelMetadataProjector().Project(document, storage, "SqlServer") with { OwnerModuleId = "Workspace" };
        }).ToArray();
    }
}
