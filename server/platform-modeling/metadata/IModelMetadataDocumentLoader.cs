namespace ICP.Platform.Modeling.Metadata;

public interface IModelMetadataDocumentLoader
{
    /// <summary>
    /// 从磁盘加载单个模型 authoring 文档。
    /// 这里要求调用方已经把文件定位到 `modules/&lt;ModuleId&gt;/metadata/**` 范围内，
    /// loader 只负责校验文件头和单业务根对象，不再依赖固定子目录名承载语义。
    /// </summary>
    ModelMetadataDocument LoadFromFile(string path);
}
