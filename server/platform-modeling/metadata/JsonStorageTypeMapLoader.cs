using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

public sealed class JsonStorageTypeMapLoader : IStorageTypeMapLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public StorageTypeMapDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);

        // 类型映射文件是平台级真源，反序列化失败时必须直接报错。
        return JsonSerializer.Deserialize<StorageTypeMapDocument>(json, Options)
            ?? throw new InvalidOperationException($"Failed to deserialize storage type map: {path}");
    }
}
