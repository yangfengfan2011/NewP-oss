using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 使用 JSON 文件加载存储类型映射文档。
/// </summary>
public sealed class JsonStorageTypeMapLoader : IStorageTypeMapLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 从指定 JSON 文件读取并反序列化存储类型映射文档。
    /// </summary>
    public StorageTypeMapDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<StorageTypeMapDocument>(json, SerializerOptions);

        return document ?? throw new InvalidOperationException("无法加载存储类型映射文档。");
    }
}
