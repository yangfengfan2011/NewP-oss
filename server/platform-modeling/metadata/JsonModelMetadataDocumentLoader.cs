using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

public sealed class JsonModelMetadataDocumentLoader : IModelMetadataDocumentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ModelMetadataDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);

        // 模型元数据文件是 authoring 真源，反序列化失败时必须直接报错。
        return JsonSerializer.Deserialize<ModelMetadataDocument>(json, Options)
            ?? throw new InvalidOperationException($"Failed to deserialize model metadata: {path}");
    }
}
