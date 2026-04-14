using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 使用 JSON 文件加载模型元数据文档。
/// </summary>
public sealed class JsonModelMetadataDocumentLoader : IModelMetadataDocumentLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 从指定 JSON 文件读取并反序列化模型元数据文档。
    /// </summary>
    public ModelMetadataDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ModelMetadataDocument>(json, SerializerOptions);

        return document ?? throw new InvalidOperationException("无法加载模型元数据文档。");
    }
}
