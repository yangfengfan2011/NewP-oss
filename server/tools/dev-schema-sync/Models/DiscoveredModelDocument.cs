using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Tools.DevSchemaSync.Models;

// 发现到的模型文档只承载扫描结果和来源路径，便于后续分层处理和回滚。
public sealed record DiscoveredModelDocument
{
    public required string ModuleId { get; init; }

    public required string ModelName { get; init; }

    public required string FilePath { get; init; }

    public required ModelMetadataDocument Document { get; init; }
}
