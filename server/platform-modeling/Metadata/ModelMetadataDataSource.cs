namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示字段的数据源定义。
/// </summary>
public sealed class ModelMetadataDataSource
{
    /// <summary>
    /// 获取或设置数据源类型，当前支持 enum 和 model。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置枚举数据源标识。
    /// </summary>
    public string? Enum { get; set; }

    /// <summary>
    /// 获取或设置模型数据源标识。
    /// </summary>
    public string? Model { get; set; }
}
