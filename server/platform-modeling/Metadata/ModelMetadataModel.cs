namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示模型主体定义。
/// </summary>
public sealed class ModelMetadataModel
{
    /// <summary>
    /// 获取或设置模型名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置模型类型，当前支持 Entity 和 Link。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置启用的可选固定字段。
    /// </summary>
    public List<string> BuiltinFields { get; set; } = [];

    /// <summary>
    /// 获取或设置业务字段定义集合。
    /// </summary>
    public List<ModelMetadataField> Fields { get; set; } = [];

    /// <summary>
    /// 获取或设置自定义索引定义集合。
    /// </summary>
    public List<ModelMetadataIndex> Indexes { get; set; } = [];

    /// <summary>
    /// 获取或设置 Link 模型允许的父子类型组合。
    /// </summary>
    public List<ModelMetadataLinkedType> LinkedTypes { get; set; } = [];
}
