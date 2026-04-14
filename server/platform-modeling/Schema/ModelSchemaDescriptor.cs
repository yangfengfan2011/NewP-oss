namespace ICP.Platform.Modeling.Schema;

/// <summary>
/// 表示面向前端 schema API 的最小模型描述对象。
/// </summary>
public sealed class ModelSchemaDescriptor
{
    /// <summary>
    /// 获取或设置模型名称。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字段 schema 集合。
    /// </summary>
    public List<ModelSchemaFieldDescriptor> Fields { get; set; } = [];
}

/// <summary>
/// 表示单个字段的最小 schema 描述。
/// </summary>
public sealed class ModelSchemaFieldDescriptor
{
    /// <summary>
    /// 获取或设置字段名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置界面类型。
    /// </summary>
    public string UiType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置多语言标签键。
    /// </summary>
    public string LabelKey { get; set; } = string.Empty;
}
