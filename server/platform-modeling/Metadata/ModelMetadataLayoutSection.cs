namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示字段的静态布局信息。
/// </summary>
public sealed class ModelMetadataLayoutSection
{
    /// <summary>
    /// 获取或设置字段位置。
    /// </summary>
    public string? Position { get; set; }

    /// <summary>
    /// 获取或设置字段宽度百分比。
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// 获取或设置所属分组标识。
    /// </summary>
    public string? Section { get; set; }
}
