namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示字段的表单行为定义。
/// </summary>
public sealed class ModelMetadataFormSection
{
    /// <summary>
    /// 获取或设置创建场景规则。
    /// </summary>
    public ModelMetadataFormModeRule? Create { get; set; }

    /// <summary>
    /// 获取或设置编辑场景规则。
    /// </summary>
    public ModelMetadataFormModeRule? Edit { get; set; }
}
