using System.Text.Json;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Persistence.DevSync.Models;

/// <summary>
/// 工具侧模型 loader，适配当前仓库的单业务根对象 authoring 形状。
/// 加载失败会保留底层 JSON/校验异常，供协调器作为失败诊断向上暴露。
/// </summary>
public sealed class SingleRootModelDocumentLoader
{
    private readonly IModelMetadataDocumentLoader _innerLoader;

    /// <summary>
    /// 创建使用标准 JSON 模型 loader 的实例。
    /// </summary>
    public SingleRootModelDocumentLoader()
        : this(new JsonModelMetadataDocumentLoader())
    {
    }

    internal SingleRootModelDocumentLoader(IModelMetadataDocumentLoader innerLoader)
    {
        _innerLoader = innerLoader;
    }

    /// <summary>
    /// 从指定路径加载模型 authoring 文档；该方法不做目录归属判断。
    /// </summary>
    /// <param name="path">模型 JSON 文件路径。</param>
    /// <returns>模型元数据文档。</returns>
    public ModelMetadataDocument LoadFromFile(string path)
    {
        return _innerLoader.LoadFromFile(path);
    }
}
