namespace ICP.Platform.Modeling.Metadata;

// display 只声明原始字段的静态展示语义，不改变字段存储或写入契约。
public sealed class ModelMetadataDisplayRule
{
    public string Type { get; init; } = string.Empty;

    public string TargetModel { get; init; } = string.Empty;

    public string TargetIdField { get; init; } = string.Empty;

    public string TargetDisplayField { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;
}
