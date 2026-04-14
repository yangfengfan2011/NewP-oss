namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 定义模型元数据文档的加载契约。
/// </summary>
public interface IModelMetadataDocumentLoader
{
    /// <summary>
    /// 从指定路径加载模型元数据文档。
    /// </summary>
    ModelMetadataDocument Load(string path);
}
