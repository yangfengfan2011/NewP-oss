namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示单模型元数据文档。
/// </summary>
public sealed class ModelMetadataDocument
{
    /// <summary>
    /// 获取或设置文档版本。
    /// </summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置所属模块标识。
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置模型主体定义。
    /// </summary>
    public ModelMetadataModel Model { get; set; } = new();
}
