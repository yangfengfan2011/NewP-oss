namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示 Link 模型允许的父子模型组合。
/// </summary>
public sealed class ModelMetadataLinkedType
{
    /// <summary>
    /// 获取或设置父对象模型名。
    /// </summary>
    public string Parent { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置子对象模型名。
    /// </summary>
    public string Child { get; set; } = string.Empty;
}
