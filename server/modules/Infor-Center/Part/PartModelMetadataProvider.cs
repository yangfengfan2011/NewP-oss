using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Part;

/// <summary>迁移与资源策略消费同一真实 authoring，不维护手写存储模型。</summary>
public sealed class PartModelMetadataProvider : IModelDefinitionProvider
{
    /// <summary>从正式输出或仓库 authoring 装载零组件字段声明。</summary>
    public ModelMetadataDocument GetDocument()
    {
        return new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(MetadataRoot(), "Part"), "metadata/models/Part.model.json"));
    }

    /// <summary>校验并投影正式 SQL Server 模型。</summary>
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var storage = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(MetadataRoot(), "server/platform-modeling/metadata/storage-types.json"));
        var document = GetDocument();
        new ModelMetadataValidator().Validate(document, storage);
        return [new ModelMetadataProjector().Project(document, storage, "SqlServer") with { OwnerModuleId = "Part" }];
    }

    // 正式输出带有同一 authoring；设计时再向上查找仓库输入，显示仍消费已编译计划。
    private static string MetadataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Part"), "metadata/models/Part.model.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位 Part 元数据。");
    }
}
