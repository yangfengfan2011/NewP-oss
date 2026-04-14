namespace ICP.Platform.Modeling.Metadata;

// 单个模型元数据文件的根文档对象。
public sealed class ModelMetadataDocument
{
    public string SchemaVersion { get; init; } = "1.0";

    public string ModuleId { get; init; } = string.Empty;

    public required ModelMetadataModel Model { get; init; }
}
