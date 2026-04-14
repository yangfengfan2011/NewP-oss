namespace ICP.Platform.Modeling.Metadata;

// linkedTypes 用于约束 Link 模型允许连接的模型类型组合。
public sealed class ModelMetadataLinkedType
{
    public string Parent { get; init; } = string.Empty;

    public string Child { get; init; } = string.Empty;
}
