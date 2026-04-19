using System.Text.Json;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Tools.DevSchemaSync.Models;

// 工具侧专用 loader，适配当前仓库的 `{ schemaVersion, type, <业务根对象> }` authoring 形状。
public sealed class SingleRootModelDocumentLoader
{
    private readonly IModelMetadataDocumentLoader _innerLoader;

    public SingleRootModelDocumentLoader()
        : this(new JsonModelMetadataDocumentLoader())
    {
    }

    internal SingleRootModelDocumentLoader(IModelMetadataDocumentLoader innerLoader)
    {
        _innerLoader = innerLoader;
    }

    public ModelMetadataDocument LoadFromFile(string path)
    {
        return _innerLoader.LoadFromFile(path);
    }
}
