using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Document;

/// <summary>迁移与资源策略消费同一真实 authoring，不维护手写存储模型。</summary>
public sealed class DocumentModelMetadataProvider : IModelDefinitionProvider
{
    /// <summary>从正式输出或仓库 authoring 装载图文档字段声明。</summary>
    public ModelMetadataDocument GetDocument()
    {
        return new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(MetadataRoot(), "Document"), "metadata/models/Document.model.json"));
    }

    /// <summary>校验并投影正式 SQL Server 模型。</summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storage = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(MetadataRoot(), "server/platform-modeling/metadata/storage-types.json"));
        var moduleRoot = ModuleAuthoringPaths.ResolveModuleRoot(MetadataRoot(), "Document");
        return new[] { "Document", "DocumentVersion", "DocumentIteration" }.Select(name =>
        {
            var document = new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(moduleRoot, "metadata/models", name + ".model.json"));
            new ModelMetadataValidator().Validate(document, storage);
            return new ModelMetadataProjector().Project(document, storage, "SqlServer") with { OwnerModuleId = "Document" };
        }).ToArray();
    }

    // 正式输出带有同一 authoring；设计时再向上查找仓库输入，显示仍消费已编译计划。
    private static string MetadataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Document"), "metadata/models/Document.model.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位 Document 元数据。");
    }
}
