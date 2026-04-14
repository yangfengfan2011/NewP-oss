namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示字段的存储分区。
/// </summary>
public sealed class ModelMetadataStorageSection
{
    /// <summary>
    /// 获取或设置平台存储类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字段是否允许为空。
    /// </summary>
    public bool Nullable { get; set; }

    /// <summary>
    /// 获取或设置最大长度。
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// 获取或设置数值精度。
    /// </summary>
    public int? Precision { get; set; }

    /// <summary>
    /// 获取或设置小数位。
    /// </summary>
    public int? Scale { get; set; }
}
