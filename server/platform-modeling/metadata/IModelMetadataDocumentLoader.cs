namespace ICP.Platform.Modeling.Metadata;

public interface IModelMetadataDocumentLoader
{
    // 从磁盘加载单个 *.model.json authoring 文档。
    ModelMetadataDocument LoadFromFile(string path);
}
