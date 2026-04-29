using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Persistence.DevSync.Models;

/// <summary>
/// 发现到的模型文档及其来源路径；只作为后续校验/投影输入，不表示运行时生效状态。
/// </summary>
public sealed record DiscoveredModelDocument
{
    /// <summary>模型所属模块 ID。</summary>
    public required string ModuleId { get; init; }

    /// <summary>模型名称。</summary>
    public required string ModelName { get; init; }

    /// <summary>模型 authoring 文件的绝对路径，用于诊断和回滚定位。</summary>
    public required string FilePath { get; init; }

    /// <summary>已加载的模型元数据文档。</summary>
    public required ModelMetadataDocument Document { get; init; }
}
