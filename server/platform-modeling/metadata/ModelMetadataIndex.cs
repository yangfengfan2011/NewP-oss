namespace ICP.Platform.Modeling.Metadata;

// 自定义索引保持在 authoring 层显式声明，内建索引仍由内核自动补入。
public sealed class ModelMetadataIndex
{
    public string Name { get; init; } = string.Empty;

    public List<string> Fields { get; init; } = [];

    public bool Unique { get; init; }
}
