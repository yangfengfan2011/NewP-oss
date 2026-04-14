namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示模型自定义索引定义。
/// </summary>
public sealed class ModelMetadataIndex
{
    /// <summary>
    /// 获取或设置索引名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置参与索引的字段集合。
    /// </summary>
    public List<string> Fields { get; set; } = [];

    /// <summary>
    /// 获取或设置索引是否唯一。
    /// </summary>
    public bool Unique { get; set; }
}
