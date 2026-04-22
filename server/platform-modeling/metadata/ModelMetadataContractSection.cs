namespace ICP.Platform.Modeling.Metadata;

// contract 只承载字段语义声明，不直接决定存储形态或生成流程。
public sealed class ModelMetadataContractSection
{
    public string? ValueType { get; init; }

    public string? ClrType { get; init; }
}
