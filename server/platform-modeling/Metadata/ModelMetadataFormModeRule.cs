namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示单个表单场景的行为模式。
/// </summary>
public sealed class ModelMetadataFormModeRule
{
    /// <summary>
    /// 获取或设置场景模式，当前支持 hidden、readonly、editable。
    /// </summary>
    public string? Mode { get; set; }
}
