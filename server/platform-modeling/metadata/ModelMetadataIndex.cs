namespace ICP.Platform.Modeling.Metadata;

// 自定义索引保持在 authoring 层显式声明，内建索引仍由内核自动补入。
public sealed class ModelMetadataIndex
{
    public string Name { get; init; } = string.Empty;

    public List<string> Fields { get; init; } = [];

    public bool Unique { get; init; }

    /// <summary>
    /// 唯一索引是否必须包含含 null 的记录；用于表达 nullable scope 中 null 也是一个受保护范围。
    /// </summary>
    public bool IncludeNullValues { get; init; }
}
