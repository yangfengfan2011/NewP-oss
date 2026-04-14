namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示模型字段定义。
/// </summary>
public sealed class ModelMetadataField
{
    /// <summary>
    /// 获取或设置字段名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置存储分区。
    /// </summary>
    public ModelMetadataStorageSection? Storage { get; set; }

    /// <summary>
    /// 获取或设置界面类型。
    /// </summary>
    public string? UiType { get; set; }

    /// <summary>
    /// 获取或设置布局定义。
    /// </summary>
    public ModelMetadataLayoutSection? Layout { get; set; }

    /// <summary>
    /// 获取或设置表单行为定义。
    /// </summary>
    public ModelMetadataFormSection? Form { get; set; }

    /// <summary>
    /// 获取或设置详情行为定义。
    /// </summary>
    public ModelMetadataViewSection? View { get; set; }

    /// <summary>
    /// 获取或设置数据源定义。
    /// </summary>
    public ModelMetadataDataSource? DataSource { get; set; }
}
