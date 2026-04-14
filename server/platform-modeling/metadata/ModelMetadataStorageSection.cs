namespace ICP.Platform.Modeling.Metadata;

// 当前最小实现中，storage 先只承载平台类型与可空性。
public sealed class ModelMetadataStorageSection
{
    public string Type { get; init; } = string.Empty;

    public bool Nullable { get; init; }
}
